using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;

namespace MIniProject.Controllers;

[Authorize(Roles = AppRoles.Staff + "," + AppRoles.Administrator)]
public class StaffController : Controller
{
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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var (ok, error) = await _bookingService.ApproveBookingAsync(id, user.Id);
        TempData[ok ? "Success" : "Error"] = ok ? "Booking approved. Member can now pay." : error;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> CheckIn(string? tab)
    {
        await PopulateCheckInViewAsync(tab);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(string passCode, string actionType, string? tab)
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

        if (MemberPassHelper.TryParseUserId(passCode, out var memberId))
        {
            await HandleMemberCheckInAsync(memberId, actionType, user.Id);
        }
        else
        {
            await HandleGuestPassCheckInAsync(passCode, actionType, user.Id);
        }

        await PopulateCheckInViewAsync(tab ?? "clearance");
        return View();
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
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Checked out guest {pass.Guest?.FullName}.";
            }
        }
        else
        {
            if (pass.CheckIns.Any(c => c.CheckedOutAt == null))
            {
                TempData["Error"] = "Guest is already checked in.";
            }
            else
            {
                _db.CheckInRecords.Add(new CheckInRecord
                {
                    VisitorPassId = pass.Id,
                    CheckedInAt = DateTime.UtcNow,
                    CheckedInByUserId = staffUserId
                });
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Checked in guest {pass.Guest?.FullName}.";
            }
        }

        ViewBag.LastGuestPass = pass;
    }

    private async Task HandleMemberCheckInAsync(string memberId, string actionType, string staffUserId)
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
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Checked out member {member.FullName}.";
            }
        }
        else
        {
            var alreadyIn = await _db.MemberCheckIns.AnyAsync(c => c.MemberId == memberId && c.CheckedOutAt == null);
            if (alreadyIn)
            {
                TempData["Error"] = "Member is already checked in.";
            }
            else
            {
                _db.MemberCheckIns.Add(new MemberCheckIn
                {
                    MemberId = memberId,
                    CheckedInAt = DateTime.UtcNow,
                    CheckedInByUserId = staffUserId
                });
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Checked in member {member.FullName} ({activeSub.MembershipPlan?.Name}).";
            }
        }

        ViewBag.LastMember = member;
        ViewBag.LastMemberPlan = activeSub.MembershipPlan?.Name;
    }

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
