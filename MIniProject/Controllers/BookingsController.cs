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

    [Authorize(Roles = AppRoles.Member)]
    [HttpGet]
    public async Task<IActionResult> Create(int facilityId, DateTime? date, string? start, string? end)
    {
        var facility = await _db.Facilities
            .Include(f => f.InventoryItems)
            .FirstOrDefaultAsync(f => f.Id == facilityId && f.IsActive);
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
            FacilityImageUrl = facility.ImageUrl,
            BookingMode = facility.BookingMode,
            HourlyRate = facility.HourlyRate,
            MaxGuests = subscription.MembershipPlan.MaxGuestsPerBooking,
            MemberDisplayName = user.FullName,
            MembershipPlanName = subscription.MembershipPlan.Name,
            DiscountPercent = subscription.MembershipPlan.DiscountPercent,
            Date = day,
            SlotStart = start ?? string.Empty,
            SlotEnd = end ?? string.Empty,
            AvailableSlots = slots.Select(s => new SlotOption
            {
                Start = s.Start.ToString("HH:mm"),
                End = s.End.ToString("HH:mm"),
                Available = s.Available,
                State = s.State
            }).ToList(),
            Equipment = facility.InventoryItems
                .Where(i => i.Condition != InventoryCondition.Retired)
                .OrderBy(i => i.Name)
                .Select(i => new EquipmentOption
                {
                    Id = i.Id,
                    Name = i.Name,
                    Quantity = i.Quantity,
                    Condition = i.Condition.ToString(),
                    DailyFee = EquipmentPricing.FeePerUnitPerDay
                }).ToList(),
            Guests = Enumerable.Range(0, Math.Min(subscription.MembershipPlan.MaxGuestsPerBooking, 4))
                .Select(_ => new GuestInputModel())
                .ToList()
        };

        return View(model);
    }

    [Authorize(Roles = AppRoles.Member)]
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

        var notes = model.Notes?.Trim();
        var selectedEquipmentIds = model.SelectedEquipmentIds.Where(id => id > 0).Distinct().ToList();

        var (ok, error, booking, emailWarning) = await _bookingService.CreateBookingAsync(
            user.Id,
            model.FacilityId,
            startLocal,
            endLocal,
            model.Guests.Select(g => (g.FullName ?? string.Empty, g.IdentificationNumber, g.Phone)),
            notes,
            model.IsRecurring,
            selectedEquipmentIds);

        if (!ok || booking is null)
        {
            TempData["Error"] = error ?? "Unable to create booking.";
            return RedirectToAction(nameof(Create), new { facilityId = model.FacilityId, date = model.Date.ToString("yyyy-MM-dd"), start = model.SlotStart, end = model.SlotEnd });
        }

        if (booking.Status == BookingStatus.Confirmed)
        {
            TempData["Success"] = "Booking confirmed. A confirmation email has been sent to your account email.";
        }
        else if (facility.BookingMode == BookingMode.ApprovalRequired)
        {
            TempData["Success"] = "Booking submitted and awaiting staff approval.";
        }
        else
        {
            TempData["Success"] = "Booking held. Please complete payment to confirm — you will receive a confirmation email after payment.";
        }

        if (!string.IsNullOrWhiteSpace(emailWarning))
        {
            TempData["Warning"] = emailWarning;
        }

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
            .Include(b => b.EquipmentRentals).ThenInclude(r => r.InventoryItem)
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

        // Safety net: cancelled bookings must not leave linked rentals Active.
        if (booking.Status == BookingStatus.Cancelled &&
            booking.EquipmentRentals.Any(r => r.Status == EquipmentRentalStatus.Active))
        {
            await _bookingService.CancelLinkedEquipmentRentalsAsync([booking.Id]);
            await _db.SaveChangesAsync();
            await _db.Entry(booking).Collection(b => b.EquipmentRentals).Query()
                .Include(r => r.InventoryItem)
                .LoadAsync();
        }

        return View(booking);
    }

    [Authorize(Roles = AppRoles.Member)]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var booking = await _db.Bookings
            .Include(b => b.Facility)!.ThenInclude(f => f!.InventoryItems)
            .Include(b => b.Guests).ThenInclude(g => g.Guest)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking is null)
        {
            return NotFound();
        }

        if (booking.MemberId != user.Id)
        {
            return Forbid();
        }

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.NoShow)
        {
            TempData["Error"] = "This booking can no longer be updated.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var subscription = await _bookingService.GetActiveSubscriptionAsync(user.Id);
        if (subscription?.MembershipPlan is null)
        {
            TempData["Error"] = "An active membership is required to update bookings.";
            return RedirectToAction("Plans", "Membership");
        }

        var day = booking.StartUtc.ToLocalTime().Date;
        var dayStart = DateTime.SpecifyKind(day, DateTimeKind.Local).ToUniversalTime();
        var dayEnd = dayStart.AddDays(1);
        var bookings = await _db.Bookings
            .Where(b => b.FacilityId == booking.FacilityId && b.Id != booking.Id && b.StartUtc < dayEnd && b.EndUtc > dayStart)
            .ToListAsync();
        var blockOuts = await _db.FacilityBlockOuts
            .Where(b => b.FacilityId == booking.FacilityId && b.StartUtc < dayEnd && b.EndUtc > dayStart)
            .ToListAsync();
        var slots = _bookingService.GetDaySlots(booking.Facility!, day, bookings, blockOuts);

        var model = new BookingCreateViewModel
        {
            FacilityId = booking.FacilityId,
            FacilityName = booking.Facility?.Name ?? string.Empty,
            FacilityImageUrl = booking.Facility?.ImageUrl,
            BookingMode = booking.Facility?.BookingMode ?? BookingMode.InstantConfirmation,
            HourlyRate = booking.Facility?.HourlyRate ?? 0,
            MaxGuests = subscription.MembershipPlan.MaxGuestsPerBooking,
            MemberDisplayName = user.FullName,
            MembershipPlanName = subscription.MembershipPlan.Name,
            DiscountPercent = subscription.MembershipPlan.DiscountPercent,
            Date = day,
            SlotStart = booking.StartUtc.ToLocalTime().ToString("HH:mm"),
            SlotEnd = booking.EndUtc.ToLocalTime().ToString("HH:mm"),
            Notes = booking.Notes,
            AvailableSlots = slots.Select(s => new SlotOption
            {
                Start = s.Start.ToString("HH:mm"),
                End = s.End.ToString("HH:mm"),
                Available = s.Available || (s.Start.ToString("HH:mm") == booking.StartUtc.ToLocalTime().ToString("HH:mm")),
                State = s.State
            }).ToList(),
            Guests = booking.Guests.Select(g => new GuestInputModel
            {
                FullName = g.Guest?.FullName,
                IdentificationNumber = g.Guest?.IdentificationNumber,
                Phone = g.Guest?.Phone
            }).ToList()
        };

        if (model.Guests.Count == 0)
        {
            model.Guests = [new GuestInputModel()];
        }

        ViewBag.BookingId = booking.Id;
        return View(model);
    }

    [Authorize(Roles = AppRoles.Member)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, BookingCreateViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!TimeSpan.TryParse(model.SlotStart, out var startTs) || !TimeSpan.TryParse(model.SlotEnd, out var endTs))
        {
            TempData["Error"] = "Please select a valid time slot.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var startLocal = model.Date.Date.Add(startTs);
        var endLocal = model.Date.Date.Add(endTs);
        var notes = model.Notes?.Trim();

        var (ok, error) = await _bookingService.UpdateBookingAsync(
            id,
            user.Id,
            startLocal,
            endLocal,
            model.Guests.Select(g => (g.FullName ?? string.Empty, g.IdentificationNumber, g.Phone)),
            notes);

        TempData[ok ? "Success" : "Error"] = ok ? "Booking updated." : error;
        return RedirectToAction(nameof(Details), new { id });
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

    [Authorize(Roles = AppRoles.Administrator)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var booking = await _db.Bookings
            .Include(b => b.Invoice)
            .Include(b => b.Guests)
            .Include(b => b.EquipmentRentals)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null)
        {
            return NotFound();
        }

        if (booking.Status != BookingStatus.Cancelled)
        {
            TempData["Error"] = "Only cancelled bookings can be hard-deleted.";
            return RedirectToAction(nameof(Details), new { id });
        }

        _db.BookingGuests.RemoveRange(booking.Guests);
        foreach (var rental in booking.EquipmentRentals)
        {
            rental.BookingId = null;
        }

        if (booking.Invoice is not null)
        {
            booking.Invoice.BookingId = null;
        }

        _db.Bookings.Remove(booking);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Cancelled booking #{id} deleted.";
        return RedirectToAction("Index", "Admin", new { tab = "bookings" });
    }

    [Authorize(Roles = AppRoles.Member)]
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
