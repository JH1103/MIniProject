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
    public IActionResult CheckIn() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(string passCode, string actionType)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var pass = await _db.VisitorPasses
            .Include(p => p.Guest)
            .Include(p => p.Booking)!.ThenInclude(b => b!.Facility)
            .Include(p => p.CheckIns)
            .FirstOrDefaultAsync(p => p.PassCode == passCode.Trim().ToUpperInvariant());

        if (pass is null)
        {
            TempData["Error"] = "Visitor pass not found.";
            return View();
        }

        if (pass.IsRevoked || DateTime.UtcNow < pass.ValidFrom || DateTime.UtcNow > pass.ValidTo)
        {
            TempData["Error"] = "Pass is not valid at this time.";
            ViewBag.Pass = pass;
            return View();
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
                open.CheckedOutByUserId = user.Id;
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Checked out {pass.Guest?.FullName}.";
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
                    CheckedInByUserId = user.Id
                });
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Checked in {pass.Guest?.FullName} in under one minute.";
            }
        }

        ViewBag.Pass = pass;
        return View();
    }
}
