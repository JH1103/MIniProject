using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;
using MIniProject.ViewModels;

namespace MIniProject.Controllers;

[Authorize]
public class BookingsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;
    private readonly PaymentService _paymentService;
    private readonly UserManager<ApplicationUser> _userManager;

    public BookingsController(
        ApplicationDbContext db,
        BookingService bookingService,
        PaymentService paymentService,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _bookingService = bookingService;
        _paymentService = paymentService;
        _userManager = userManager;
    }

    [Authorize(Roles = AppRoles.Member + "," + AppRoles.Administrator)]
    [HttpGet]
    public async Task<IActionResult> Create(int facilityId, DateTime? date, string? start, string? end)
    {
        var facility = await _db.Facilities.FirstOrDefaultAsync(f => f.Id == facilityId && f.IsActive);
        if (facility is null)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var subscription = await _bookingService.GetActiveSubscriptionAsync(user.Id);
        if (subscription?.MembershipPlan is null)
        {
            TempData["Error"] = "You need an active membership before booking.";
            return RedirectToAction("Plans", "Membership");
        }

        var day = (date ?? DateTime.Today).Date;
        var dayStart = DateTime.SpecifyKind(day, DateTimeKind.Local).ToUniversalTime();
        var dayEnd = dayStart.AddDays(1);
        var bookings = await _db.Bookings.Where(b => b.FacilityId == facilityId && b.StartUtc < dayEnd && b.EndUtc > dayStart).ToListAsync();
        var blockOuts = await _db.FacilityBlockOuts.Where(b => b.FacilityId == facilityId && b.StartUtc < dayEnd && b.EndUtc > dayStart).ToListAsync();
        var slots = _bookingService.GetDaySlots(facility, day, bookings, blockOuts);

        var model = new BookingCreateViewModel
        {
            FacilityId = facility.Id,
            FacilityName = facility.Name,
            BookingMode = facility.BookingMode,
            HourlyRate = facility.HourlyRate,
            MaxGuests = subscription.MembershipPlan.MaxGuestsPerBooking,
            Date = day,
            SlotStart = start ?? string.Empty,
            SlotEnd = end ?? string.Empty,
            AvailableSlots = slots.Select(s => new SlotOption
            {
                Start = s.Start.ToString("HH:mm"),
                End = s.End.ToString("HH:mm"),
                Available = s.Available
            }).ToList(),
            Guests = Enumerable.Range(0, Math.Min(subscription.MembershipPlan.MaxGuestsPerBooking, 4))
                .Select(_ => new GuestInputModel())
                .ToList()
        };

        return View(model);
    }

    [Authorize(Roles = AppRoles.Member + "," + AppRoles.Administrator)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BookingCreateViewModel model)
    {
        var facility = await _db.Facilities.FirstOrDefaultAsync(f => f.Id == model.FacilityId && f.IsActive);
        if (facility is null)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!TimeSpan.TryParse(model.SlotStart, out var startTs) || !TimeSpan.TryParse(model.SlotEnd, out var endTs))
        {
            ModelState.AddModelError(string.Empty, "Please select a valid time slot.");
            return await Create(model.FacilityId, model.Date, model.SlotStart, model.SlotEnd);
        }

        if (!ModelState.IsValid)
        {
            return await Create(model.FacilityId, model.Date, model.SlotStart, model.SlotEnd);
        }

        var startLocal = model.Date.Date.Add(startTs);
        var endLocal = model.Date.Date.Add(endTs);

        var (ok, error, booking) = await _bookingService.CreateBookingAsync(
            user.Id,
            model.FacilityId,
            startLocal,
            endLocal,
            model.Guests.Select(g => (g.FullName ?? string.Empty, g.IdentificationNumber, g.Phone)));

        if (!ok || booking is null)
        {
            TempData["Error"] = error ?? "Unable to create booking.";
            return RedirectToAction(nameof(Create), new { facilityId = model.FacilityId, date = model.Date.ToString("yyyy-MM-dd") });
        }

        TempData["Success"] = facility.BookingMode == BookingMode.ApprovalRequired
            ? "Booking submitted and awaiting staff approval."
            : "Booking held. Please complete payment to confirm.";

        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.BookingId == booking.Id);
        if (invoice is not null && invoice.Status == InvoiceStatus.Unpaid)
        {
            return RedirectToAction("Pay", "Payments", new { id = invoice.Id });
        }

        return RedirectToAction("Details", new { id = booking.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var booking = await _db.Bookings
            .Include(b => b.Facility)
            .Include(b => b.Invoice)
            .Include(b => b.Guests).ThenInclude(bg => bg.Guest)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking is null)
        {
            return NotFound();
        }

        var isStaff = User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator);
        if (!isStaff && booking.MemberId != user.Id)
        {
            return Forbid();
        }

        return View(booking);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var byClubhouse = User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator);
        var (ok, error) = await _paymentService.RequestRefundAsync(id, user.Id, reason ?? "Member cancellation", byClubhouse);
        TempData[ok ? "Success" : "Error"] = ok ? "Booking cancelled." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = AppRoles.Member + "," + AppRoles.Administrator)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> JoinWaitingList(int facilityId, DateTime date, string start, string end)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!TimeSpan.TryParse(start, out var startTs) || !TimeSpan.TryParse(end, out var endTs))
        {
            TempData["Error"] = "Invalid slot.";
            return RedirectToAction("Details", "Facilities", new { id = facilityId, date = date.ToString("yyyy-MM-dd") });
        }

        var startLocal = date.Date.Add(startTs);
        var endLocal = date.Date.Add(endTs);

        _db.WaitingListEntries.Add(new WaitingListEntry
        {
            MemberId = user.Id,
            FacilityId = facilityId,
            DesiredStartUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime(),
            DesiredEndUtc = DateTime.SpecifyKind(endLocal, DateTimeKind.Local).ToUniversalTime()
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "You have been added to the waiting list for this slot.";
        return RedirectToAction("Details", "Facilities", new { id = facilityId, date = date.ToString("yyyy-MM-dd") });
    }
}
