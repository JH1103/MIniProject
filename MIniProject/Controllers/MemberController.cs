using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;
using MIniProject.ViewModels;
using QRCoder;

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

        var subscription = await _bookingService.GetActiveSubscriptionAsync(user.Id);
        var passCode = MemberPassHelper.BuildPassCode(user.Id);
        var qrBase64 = string.Empty;

        if (subscription is not null)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(passCode, QRCodeGenerator.ECCLevel.Q);
            var qr = new PngByteQRCode(data);
            qrBase64 = Convert.ToBase64String(qr.GetGraphic(8));
        }

        var upgradePlans = new List<MembershipPlan>();
        if (subscription?.MembershipPlan is not null)
        {
            upgradePlans = await _db.MembershipPlans
                .Where(p => p.IsActive && p.Id != subscription.MembershipPlanId && p.AnnualFee > subscription.MembershipPlan.AnnualFee)
                .OrderBy(p => p.AnnualFee)
                .ToListAsync();

            // If already on top plan, still show other active plans for comparison.
            if (upgradePlans.Count == 0)
            {
                upgradePlans = await _db.MembershipPlans
                    .Where(p => p.IsActive && p.Id != subscription.MembershipPlanId)
                    .OrderBy(p => p.AnnualFee)
                    .ToListAsync();
            }
        }
        else
        {
            upgradePlans = await _db.MembershipPlans.Where(p => p.IsActive).OrderBy(p => p.AnnualFee).ToListAsync();
        }

        var model = new MemberDashboardViewModel
        {
            User = user,
            Subscription = subscription,
            MemberPassCode = passCode,
            MemberQrBase64 = qrBase64,
            UpgradePlans = upgradePlans,
            UpcomingBookings = await _db.Bookings
                .Include(b => b.Facility)
                .Where(b => b.MemberId == user.Id &&
                            b.EndUtc >= DateTime.UtcNow &&
                            b.Status != BookingStatus.Cancelled &&
                            b.Status != BookingStatus.Completed &&
                            b.Status != BookingStatus.NoShow)
                .OrderBy(b => b.StartUtc)
                .Take(10)
                .ToListAsync(),
            BookingHistory = await _db.Bookings
                .Include(b => b.Facility)
                .Where(b => b.MemberId == user.Id &&
                            (b.Status == BookingStatus.Cancelled ||
                             b.Status == BookingStatus.Completed ||
                             b.Status == BookingStatus.NoShow ||
                             b.EndUtc < DateTime.UtcNow))
                .OrderByDescending(b => b.StartUtc)
                .Take(10)
                .ToListAsync(),
            EquipmentRentals = await SyncMemberRentalsAsync(user.Id),
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

    private async Task<List<EquipmentRental>> SyncMemberRentalsAsync(string memberId)
    {
        var rentals = await _db.EquipmentRentals
            .Include(r => r.InventoryItem)!.ThenInclude(i => i!.Facility)
            .Include(r => r.Booking)
            .Where(r => r.MemberId == memberId)
            .OrderBy(r =>
                r.Status == EquipmentRentalStatus.Active ||
                r.Status == EquipmentRentalStatus.Rented ||
                r.Status == EquipmentRentalStatus.NotReturned
                    ? 0
                    : 1)
            .ThenByDescending(r => r.CreatedAt)
            .Take(10)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var rental in rentals)
        {
            if (EquipmentRentalLifecycle.ApplyTimeBasedTransitions(rental, now))
            {
                changed = true;
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync();
        }

        return rentals;
    }
}
