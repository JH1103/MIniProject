using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;

namespace MIniProject.Controllers;

public class MembershipController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public MembershipController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Plans()
    {
        var plans = await _db.MembershipPlans.Where(p => p.IsActive).OrderBy(p => p.AnnualFee).ToListAsync();
        return View(plans);
    }

    [Authorize(Roles = AppRoles.Member + "," + AppRoles.Administrator)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(int planId)
    {
        var plan = await _db.MembershipPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);
        if (plan is null)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var subscription = new Subscription
        {
            UserId = user.Id,
            MembershipPlanId = plan.Id,
            StartDate = DateTime.UtcNow.Date,
            EndDate = DateTime.UtcNow.Date.AddYears(1),
            Status = MembershipStatus.Suspended
        };
        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = $"MEM-{DateTime.UtcNow:yyyyMMdd}-{subscription.Id:D5}",
            UserId = user.Id,
            SubscriptionId = subscription.Id,
            Amount = plan.AnnualFee,
            Status = InvoiceStatus.Unpaid,
            Description = $"{plan.Name} membership (1 year)",
            DueAt = DateTime.UtcNow.AddDays(3)
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        return RedirectToAction("Pay", "Payments", new { id = invoice.Id });
    }
}
