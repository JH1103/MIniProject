using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;
using MIniProject.ViewModels;

namespace MIniProject.Controllers;

[Authorize(Roles = AppRoles.Staff + "," + AppRoles.Administrator)]
public class StaffController : Controller
{
    public const int EarlyCheckInMinutes = 15;

    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public StaffController(ApplicationDbContext db, BookingService bookingService, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _bookingService = bookingService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        await _bookingService.ReleaseExpiredHoldsAsync();

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        ViewBag.TodayBookings = await _db.Bookings
            .Include(b => b.Facility)
            .Include(b => b.Member)
            .Where(b => b.StartUtc >= today && b.StartUtc < tomorrow && b.Status != BookingStatus.Cancelled)
            .OrderBy(b => b.StartUtc)
            .ToListAsync();

        ViewBag.PendingApprovals = await _db.Bookings
            .Include(b => b.Facility)
            .Include(b => b.Member)
            .Where(b => b.Status == BookingStatus.Pending)
            .OrderBy(b => b.StartUtc)
            .ToListAsync();

        ViewBag.FacilityStatus = await _db.Facilities.Where(f => f.IsActive).OrderBy(f => f.Name).ToListAsync();
        return View();
    }

    /// <summary>Staff may view membership plans and member subscriptions only (no create/update/delete).</summary>
    [HttpGet]
    public async Task<IActionResult> Membership()
    {
        ViewBag.Plans = await _db.MembershipPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.AnnualFee)
            .ToListAsync();

        ViewBag.Subscriptions = await _db.Subscriptions
            .Include(s => s.MembershipPlan)
            .Include(s => s.User)
            .OrderByDescending(s => s.CreatedAt)
            .Take(50)
            .ToListAsync();

        ViewBag.Members = await _db.Users
            .OrderBy(u => u.FullName)
            .Take(50)
            .ToListAsync();

        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var (ok, error, emailWarning) = await _bookingService.ApproveBookingAsync(id, user.Id);
        if (!ok)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index));
        }

        var booking = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        TempData["Success"] = booking?.Status == BookingStatus.Confirmed
            ? "Booking approved and confirmed. A confirmation email was sent to the member."
            : "Booking approved. Member can now pay — they will receive a confirmation email after payment.";
        if (!string.IsNullOrWhiteSpace(emailWarning))
        {
            TempData["Warning"] = emailWarning;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> CheckIn(string? tab)
    {
        await PopulateCheckInViewAsync(tab);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(string passCode, string actionType, string? tab, int? bookingId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(passCode))
        {
            TempData["Error"] = "Enter a pass code or member QR code.";
            await PopulateCheckInViewAsync(tab);
            return View();
        }

        var normalizedAction = (actionType ?? "lookup").Trim().ToLowerInvariant();

        if (MemberPassHelper.TryParseUserId(passCode, out var memberId))
        {
            if (normalizedAction is "in" or "out")
            {
                await HandleMemberCheckInAsync(memberId, normalizedAction, user.Id, bookingId);
            }
            else
            {
                await BuildMemberPreviewAsync(memberId, passCode.Trim());
            }
        }
        else
        {
            if (normalizedAction is "in" or "out")
            {
                await HandleGuestPassCheckInAsync(passCode, normalizedAction, user.Id);
            }
            else
            {
                await BuildGuestPreviewAsync(passCode);
            }
        }

        await PopulateCheckInViewAsync(tab ?? "clearance");
        return View();
    }

    private async Task BuildMemberPreviewAsync(string memberId, string passCode)
    {
        var member = await _db.Users.FirstOrDefaultAsync(u => u.Id == memberId);
        if (member is null)
        {
            TempData["Error"] = "Member digital card not recognised.";
            return;
        }

        var activeSub = await _bookingService.GetActiveSubscriptionAsync(memberId);
        if (activeSub is null)
        {
            TempData["Error"] = $"{member.FullName} has no active membership.";
            return;
        }

        var alreadyIn = await _db.MemberCheckIns.AnyAsync(c => c.MemberId == memberId && c.CheckedOutAt == null);
        var todayBookings = await GetTodayBookingsForMemberAsync(memberId);
        var (canCheckIn, selectedBooking, blockReason, prompt) = EvaluateCheckInWindow(todayBookings);

        ViewBag.ScanPreview = new CheckInPreviewViewModel
        {
            PassCode = passCode,
            IsMember = true,
            DisplayName = member.FullName,
            PlanName = activeSub.MembershipPlan?.Name,
            PlanValidUntil = activeSub.EndDate,
            Identification = member.IdentificationNumber,
            AlreadyInside = alreadyIn,
            CanCheckIn = canCheckIn && !alreadyIn,
            BlockReason = alreadyIn ? "Member is already checked in." : blockReason,
            PromptMessage = alreadyIn ? null : prompt,
            SelectedBookingId = selectedBooking?.Id,
            TodayBookings = todayBookings.Select(ToBookingRow).ToList()
        };

        ViewBag.LastMember = member;
        ViewBag.LastMemberPlan = activeSub.MembershipPlan?.Name;
        // Preview panel already shows prompt/block reason — avoid duplicate flash banners.
    }

    private async Task BuildGuestPreviewAsync(string passCode)
    {
        var pass = await _db.VisitorPasses
            .Include(p => p.Guest)
            .Include(p => p.Booking)!.ThenInclude(b => b!.Facility)
            .Include(p => p.CheckIns)
            .FirstOrDefaultAsync(p => p.PassCode == passCode.Trim().ToUpperInvariant());

        if (pass is null)
        {
            TempData["Error"] = "Visitor pass or member card not found.";
            return;
        }

        if (pass.IsRevoked || DateTime.UtcNow < pass.ValidFrom || DateTime.UtcNow > pass.ValidTo)
        {
            TempData["Error"] = "Pass is not valid at this time.";
            ViewBag.LastGuestPass = pass;
            return;
        }

        var alreadyIn = pass.CheckIns.Any(c => c.CheckedOutAt == null);
        var bookings = new List<Booking>();
        if (pass.Booking is not null && pass.Booking.Status != BookingStatus.Cancelled)
        {
            bookings.Add(pass.Booking);
        }

        var (canCheckIn, selectedBooking, blockReason, prompt) = EvaluateCheckInWindow(bookings, requireToday: true);

        ViewBag.ScanPreview = new CheckInPreviewViewModel
        {
            PassCode = pass.PassCode,
            IsMember = false,
            DisplayName = pass.Guest?.FullName ?? "Guest",
            Identification = pass.Guest?.IdentificationNumber,
            AlreadyInside = alreadyIn,
            CanCheckIn = canCheckIn && !alreadyIn,
            BlockReason = alreadyIn ? "Guest is already checked in." : blockReason,
            PromptMessage = alreadyIn ? null : prompt,
            SelectedBookingId = selectedBooking?.Id ?? pass.BookingId,
            TodayBookings = bookings.Select(ToBookingRow).ToList()
        };

        ViewBag.LastGuestPass = pass;
        // Preview panel already shows prompt/block reason — avoid duplicate flash banners.
    }

    private async Task HandleGuestPassCheckInAsync(string passCode, string actionType, string staffUserId)
    {
        var pass = await _db.VisitorPasses
            .Include(p => p.Guest)
            .Include(p => p.Booking)!.ThenInclude(b => b!.Facility)
            .Include(p => p.CheckIns)
            .FirstOrDefaultAsync(p => p.PassCode == passCode.Trim().ToUpperInvariant());

        if (pass is null)
        {
            TempData["Error"] = "Visitor pass or member card not found.";
            return;
        }

        if (pass.IsRevoked || DateTime.UtcNow < pass.ValidFrom || DateTime.UtcNow > pass.ValidTo)
        {
            TempData["Error"] = "Pass is not valid at this time.";
            ViewBag.LastGuestPass = pass;
            return;
        }

        if (string.Equals(actionType, "out", StringComparison.OrdinalIgnoreCase))
        {
            var open = pass.CheckIns.OrderByDescending(c => c.CheckedInAt).FirstOrDefault(c => c.CheckedOutAt == null);
            if (open is null)
            {
                TempData["Error"] = "No open check-in found for this pass.";
            }
            else
            {
                open.CheckedOutAt = DateTime.UtcNow;
                open.CheckedOutByUserId = staffUserId;
                open.CheckedOutByName = await ResolveStaffNameAsync(staffUserId);
                open.Status = ClearanceStatus.CheckedOut;
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Checked out guest {pass.Guest?.FullName}.";
            }

            ViewBag.LastGuestPass = pass;
            return;
        }

        var bookings = pass.Booking is not null && pass.Booking.Status != BookingStatus.Cancelled
            ? new List<Booking> { pass.Booking }
            : [];
        var (canCheckIn, _, blockReason, prompt) = EvaluateCheckInWindow(bookings, requireToday: true);
        if (!canCheckIn)
        {
            TempData[string.IsNullOrWhiteSpace(prompt) ? "Error" : "Warning"] = prompt ?? blockReason ?? "Check-in not allowed.";
            await BuildGuestPreviewAsync(passCode);
            return;
        }

        if (pass.CheckIns.Any(c => c.CheckedOutAt == null))
        {
            TempData["Error"] = "Guest is already checked in.";
            await BuildGuestPreviewAsync(passCode);
            return;
        }

        var staffName = await ResolveStaffNameAsync(staffUserId);
        _db.CheckInRecords.Add(new CheckInRecord
        {
            VisitorPassId = pass.Id,
            CheckedInAt = DateTime.UtcNow,
            CheckedInByUserId = staffUserId,
            CheckedInByName = staffName,
            Status = ClearanceStatus.UsingFacility
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Checked in guest {pass.Guest?.FullName} for {pass.Booking?.Facility?.Name ?? "facility"}.";
        ViewBag.LastGuestPass = pass;
        ViewBag.ScanPreview = null;
    }

    private async Task HandleMemberCheckInAsync(string memberId, string actionType, string staffUserId, int? bookingId)
    {
        var member = await _db.Users.FirstOrDefaultAsync(u => u.Id == memberId);
        if (member is null)
        {
            TempData["Error"] = "Member digital card not recognised.";
            return;
        }

        var activeSub = await _bookingService.GetActiveSubscriptionAsync(memberId);
        if (activeSub is null)
        {
            TempData["Error"] = $"{member.FullName} has no active membership.";
            return;
        }

        if (string.Equals(actionType, "out", StringComparison.OrdinalIgnoreCase))
        {
            var open = await _db.MemberCheckIns
                .Where(c => c.MemberId == memberId && c.CheckedOutAt == null)
                .OrderByDescending(c => c.CheckedInAt)
                .FirstOrDefaultAsync();
            if (open is null)
            {
                TempData["Error"] = "No open member check-in found.";
            }
            else
            {
                open.CheckedOutAt = DateTime.UtcNow;
                open.CheckedOutByUserId = staffUserId;
                open.CheckedOutByName = await ResolveStaffNameAsync(staffUserId);
                open.Status = ClearanceStatus.CheckedOut;
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Checked out member {member.FullName}.";
            }

            ViewBag.LastMember = member;
            ViewBag.LastMemberPlan = activeSub.MembershipPlan?.Name;
            return;
        }

        var todayBookings = await GetTodayBookingsForMemberAsync(memberId);
        var (canCheckIn, selectedBooking, blockReason, prompt) = EvaluateCheckInWindow(todayBookings);

        if (bookingId is int requestedId)
        {
            selectedBooking = todayBookings.FirstOrDefault(b => b.Id == requestedId) ?? selectedBooking;
            if (selectedBooking is Booking chosen)
            {
                (canCheckIn, selectedBooking, blockReason, prompt) = EvaluateCheckInWindow([chosen]);
            }
        }

        if (!canCheckIn || selectedBooking is null)
        {
            TempData[string.IsNullOrWhiteSpace(prompt) ? "Error" : "Warning"] = prompt ?? blockReason ?? "Check-in not allowed.";
            await BuildMemberPreviewAsync(memberId, MemberPassHelper.BuildPassCode(memberId));
            return;
        }

        var alreadyIn = await _db.MemberCheckIns.AnyAsync(c => c.MemberId == memberId && c.CheckedOutAt == null);
        if (alreadyIn)
        {
            TempData["Error"] = "Member is already checked in.";
            await BuildMemberPreviewAsync(memberId, MemberPassHelper.BuildPassCode(memberId));
            return;
        }

        var staffName = await ResolveStaffNameAsync(staffUserId);
        _db.MemberCheckIns.Add(new MemberCheckIn
        {
            MemberId = memberId,
            BookingId = selectedBooking.Id,
            FacilityId = selectedBooking.FacilityId,
            CheckedInAt = DateTime.UtcNow,
            CheckedInByUserId = staffUserId,
            CheckedInByName = staffName,
            Status = ClearanceStatus.UsingFacility
        });
        await _db.SaveChangesAsync();
        TempData["Success"] =
            $"Checked in member {member.FullName} for {selectedBooking.Facility?.Name ?? "facility"} ({activeSub.MembershipPlan?.Name}).";

        ViewBag.LastMember = member;
        ViewBag.LastMemberPlan = activeSub.MembershipPlan?.Name;
        ViewBag.ScanPreview = null;
    }

    private async Task<string?> ResolveStaffNameAsync(string staffUserId)
    {
        return await _db.Users
            .Where(u => u.Id == staffUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync();
    }

    private async Task<List<Booking>> GetTodayBookingsForMemberAsync(string memberId)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        return await _db.Bookings
            .Include(b => b.Facility)
            .Where(b => b.MemberId == memberId
                        && b.StartUtc >= today
                        && b.StartUtc < tomorrow
                        && b.Status != BookingStatus.Cancelled
                        && b.Status != BookingStatus.NoShow)
            .OrderBy(b => b.StartUtc)
            .ToListAsync();
    }

    private static (bool CanCheckIn, Booking? Booking, string? BlockReason, string? Prompt) EvaluateCheckInWindow(
        IReadOnlyList<Booking> bookings,
        bool requireToday = true)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var tomorrow = today.AddDays(1);

        var todayBookings = bookings
            .Where(b => !requireToday
                        || (b.StartUtc >= today && b.StartUtc < tomorrow)
                        || (b.EndUtc >= today && b.StartUtc < tomorrow))
            .OrderBy(b => b.StartUtc)
            .ToList();

        if (todayBookings.Count == 0)
        {
            return (false, null, "No booking on the day — entry is not allowed.", null);
        }

        var confirmed = todayBookings.Where(b => b.Status == BookingStatus.Confirmed).ToList();
        if (confirmed.Count == 0)
        {
            var pendingLike = todayBookings.FirstOrDefault(b =>
                b.Status is BookingStatus.Pending or BookingStatus.AwaitingPayment);
            if (pendingLike is not null)
            {
                return (false, pendingLike,
                    $"Booking for {pendingLike.Facility?.Name ?? "facility"} is {pendingLike.Status} — confirm payment/approval before check-in.",
                    null);
            }

            return (false, todayBookings.First(), "No confirmed booking on the day — entry is not allowed.", null);
        }

        foreach (var booking in confirmed)
        {
            var opensAt = booking.StartUtc.AddMinutes(-EarlyCheckInMinutes);
            if (now >= opensAt && now <= booking.EndUtc)
            {
                return (true, booking, null, null);
            }
        }

        var upcoming = confirmed.FirstOrDefault(b => now < b.StartUtc.AddMinutes(-EarlyCheckInMinutes));
        if (upcoming is not null)
        {
            var opensLocal = upcoming.StartUtc.AddMinutes(-EarlyCheckInMinutes).ToLocalTime();
            var startLocal = upcoming.StartUtc.ToLocalTime();
            var facility = upcoming.Facility?.Name ?? "facility";
            var prompt =
                $"Too early to check in. {facility} starts at {startLocal:HH:mm}. Check-in opens 15 minutes before ({opensLocal:HH:mm}).";
            return (false, upcoming, null, prompt);
        }

        return (false, confirmed.Last(), "Today's booking window has already ended.", null);
    }

    private static CheckInBookingRow ToBookingRow(Booking booking)
    {
        var now = DateTime.UtcNow;
        var opensAt = booking.StartUtc.AddMinutes(-EarlyCheckInMinutes);
        return new CheckInBookingRow
        {
            BookingId = booking.Id,
            FacilityName = booking.Facility?.Name ?? "Facility",
            StartUtc = booking.StartUtc,
            EndUtc = booking.EndUtc,
            Status = booking.Status,
            CheckInOpensUtc = opensAt,
            IsEligibleWindow = now >= opensAt && now <= booking.EndUtc && booking.Status == BookingStatus.Confirmed
        };
    }

    public static string FormatClearanceStatus(ClearanceStatus status) => status switch
    {
        ClearanceStatus.CheckedIn => "Checked in",
        ClearanceStatus.UsingFacility => "Using facility",
        ClearanceStatus.CheckedOut => "Checked out",
        ClearanceStatus.Denied => "Denied",
        _ => status.ToString()
    };

    private async Task PopulateCheckInViewAsync(string? tab)
    {
        var selected = string.IsNullOrWhiteSpace(tab) ? "clearance" : tab.Trim().ToLowerInvariant();
        ViewBag.SelectedTab = selected;

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        ViewBag.TodaySlots = await _db.Bookings.CountAsync(b =>
            b.StartUtc >= today && b.StartUtc < tomorrow && b.Status != BookingStatus.Cancelled);

        var pending = await _db.Bookings
            .Include(b => b.Facility)
            .Include(b => b.Member)
            .Where(b => b.Status == BookingStatus.Pending)
            .OrderBy(b => b.StartUtc)
            .ToListAsync();
        ViewBag.PendingApprovals = pending;
        ViewBag.PendingCount = pending.Count;

        ViewBag.TodayBookings = await _db.Bookings
            .Include(b => b.Facility)
            .Include(b => b.Member)
            .Where(b => b.StartUtc >= today && b.StartUtc < tomorrow && b.Status != BookingStatus.Cancelled)
            .OrderBy(b => b.StartUtc)
            .ToListAsync();

        ViewBag.InsideGuests = await _db.CheckInRecords
            .Include(c => c.VisitorPass)!.ThenInclude(p => p!.Guest)
            .Include(c => c.VisitorPass)!.ThenInclude(p => p!.Booking)!.ThenInclude(b => b!.Facility)
            .Where(c => c.CheckedOutAt == null)
            .OrderByDescending(c => c.CheckedInAt)
            .Take(30)
            .ToListAsync();

        ViewBag.InsideMembers = await _db.MemberCheckIns
            .Include(c => c.Member)
            .Include(c => c.Facility)
            .Include(c => c.Booking)!.ThenInclude(b => b!.Facility)
            .Where(c => c.CheckedOutAt == null)
            .OrderByDescending(c => c.CheckedInAt)
            .Take(30)
            .ToListAsync();

        ViewBag.DemoPasses = await _db.VisitorPasses
            .Include(p => p.Guest)
            .Where(p => !p.IsRevoked && p.ValidTo >= DateTime.UtcNow)
            .OrderByDescending(p => p.ValidFrom)
            .Take(3)
            .ToListAsync();
    }
}
