using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;
using MIniProject.ViewModels;

namespace MIniProject.Controllers;

[Authorize(Roles = AppRoles.Member + "," + AppRoles.Administrator)]
public class MemberController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BookingService _bookingService;

    public MemberController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, BookingService bookingService)
    {
        _db = db;
        _userManager = userManager;
        _bookingService = bookingService;
    }

    public async Task<IActionResult> Dashboard()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await _bookingService.ReleaseExpiredHoldsAsync();

        var model = new MemberDashboardViewModel
        {
            User = user,
            Subscription = await _bookingService.GetActiveSubscriptionAsync(user.Id),
            UpcomingBookings = await _db.Bookings
                .Include(b => b.Facility)
                .Where(b => b.MemberId == user.Id && b.EndUtc >= DateTime.UtcNow && b.Status != BookingStatus.Cancelled)
                .OrderBy(b => b.StartUtc)
                .Take(10)
                .ToListAsync(),
            Invoices = await _db.Invoices
                .Where(i => i.UserId == user.Id)
                .OrderByDescending(i => i.IssuedAt)
                .Take(8)
                .ToListAsync(),
            Passes = await _db.VisitorPasses
                .Include(p => p.Guest)
                .Where(p => p.Guest!.HostMemberId == user.Id && p.ValidTo >= DateTime.UtcNow && !p.IsRevoked)
                .OrderBy(p => p.ValidFrom)
                .Take(10)
                .ToListAsync()
        };

        return View(model);
    }
}
