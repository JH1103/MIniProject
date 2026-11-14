using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;
using MIniProject.ViewModels;

namespace MIniProject.Controllers;

public class MembershipController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BookingService _bookingService;

    public MembershipController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, BookingService bookingService)
    {
        _db = db;
        _userManager = userManager;
        _bookingService = bookingService;
    }

    public async Task<IActionResult> Plans(bool change = false)
    {
        var plans = await _db.MembershipPlans.Where(p => p.IsActive).OrderBy(p => p.AnnualFee).ToListAsync();
        ViewBag.ChangeMode = change;

        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is not null)
            {
                ViewBag.CurrentSubscription = await _bookingService.GetActiveSubscriptionAsync(user.Id);
            }
        }

        return View(plans);
    }

    [Authorize(Roles = AppRoles.Member + "," + AppRoles.Administrator)]
    [HttpGet]
    public async Task<IActionResult> Subscribe(int planId)
    {
        var plan = await _db.MembershipPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);
        if (plan is null)
        {
            return NotFound();
        }

        return View(new SubscribeConfirmViewModel
        {
            PlanId = plan.Id,
            PlanName = plan.Name,
            Description = plan.Description,
            AnnualFee = plan.AnnualFee,
            AdvanceBookingDays = plan.AdvanceBookingDays,
            MaxGuestsPerBooking = plan.MaxGuestsPerBooking,
            DiscountPercent = plan.DiscountPercent,
            AutoRenew = true
        });
    }

    [Authorize(Roles = AppRoles.Member + "," + AppRoles.Administrator)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmSubscribe(SubscribeConfirmViewModel model)
    {
        var plan = await _db.MembershipPlans.FirstOrDefaultAsync(p => p.Id == model.PlanId && p.IsActive);
        if (plan is null)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        model.PlanName = plan.Name;
        model.Description = plan.Description;
        model.AnnualFee = plan.AnnualFee;
        model.AdvanceBookingDays = plan.AdvanceBookingDays;
        model.MaxGuestsPerBooking = plan.MaxGuestsPerBooking;
        model.DiscountPercent = plan.DiscountPercent;

        if (!ModelState.IsValid)
        {
            return View("Subscribe", model);
        }

        var previous = await _db.Subscriptions
            .Where(s => s.UserId == user.Id &&
                        (s.Status == MembershipStatus.Active || s.Status == MembershipStatus.Suspended) &&
                        s.CancelledAt == null)
            .ToListAsync();
        foreach (var old in previous)
        {
            old.AutoRenew = false;
            old.CancelledAt = DateTime.UtcNow;
            if (old.Status == MembershipStatus.Suspended)
            {
                old.Status = MembershipStatus.Expired;
                old.EndDate = DateTime.UtcNow.Date;
            }
            // Active plans keep access until their EndDate; new paid plan becomes the managed one after payment.
        }

        var subscription = new Subscription
        {
            UserId = user.Id,
            MembershipPlanId = plan.Id,
            StartDate = DateTime.UtcNow.Date,
            EndDate = DateTime.UtcNow.Date.AddYears(1),
            Status = MembershipStatus.Suspended,
            AutoRenew = model.AutoRenew
        };
        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        var renewNote = model.AutoRenew ? " · auto-renewal on" : " · auto-renewal off";
        var invoice = new Invoice
        {
            InvoiceNumber = $"MEM-{DateTime.UtcNow:yyyyMMdd}-{subscription.Id:D5}",
            UserId = user.Id,
            SubscriptionId = subscription.Id,
            Amount = plan.AnnualFee,
            Status = InvoiceStatus.Unpaid,
            Description = $"{plan.Name} membership (1 year){renewNote}",
            DueAt = DateTime.UtcNow.AddDays(3)
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        TempData["Success"] = model.AutoRenew
            ? "Auto-renewal enabled. Complete payment to activate your membership."
            : "Auto-renewal disabled. Complete payment to activate your membership for one year.";

        return RedirectToAction("Pay", "Payments", new { id = invoice.Id });
    }

    [Authorize(Roles = AppRoles.Member + "," + AppRoles.Administrator)]
    [HttpGet]
    public async Task<IActionResult> Manage()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var active = await _bookingService.GetActiveSubscriptionAsync(user.Id);
        var history = await _db.Subscriptions
            .Include(s => s.MembershipPlan)
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt)
            .Take(10)
            .ToListAsync();

        return View(new MembershipManageViewModel
        {
            Subscription = active,
            History = history
        });
    }

    [Authorize(Roles = AppRoles.Member + "," + AppRoles.Administrator)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAutoRenew(int id, bool autoRenew)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id);
        if (subscription is null)
        {
            return NotFound();
        }

        if (subscription.Status != MembershipStatus.Active)
        {
            TempData["Error"] = "Only an active membership can change auto-renewal.";
            return RedirectToAction(nameof(Manage));
        }

        if (subscription.CancelledAt is not null)
        {
            TempData["Error"] = "This membership was unsubscribed. Start a new plan to renew.";
            return RedirectToAction(nameof(Manage));
        }

        subscription.AutoRenew = autoRenew;
        await _db.SaveChangesAsync();
        TempData["Success"] = autoRenew
            ? "Auto-renewal turned on. Your plan will renew at expiry."
            : "Auto-renewal turned off. Your plan will end on the expiry date.";
        return RedirectToAction(nameof(Manage));
    }

    [Authorize(Roles = AppRoles.Member + "," + AppRoles.Administrator)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unsubscribe(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var subscription = await _db.Subscriptions
            .Include(s => s.MembershipPlan)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id);

        if (subscription is null)
        {
            return NotFound();
        }

        if (subscription.CancelledAt is not null || subscription.Status == MembershipStatus.Expired)
        {
            TempData["Error"] = "This membership is already cancelled or inactive.";
            return RedirectToAction(nameof(Manage));
        }

        // Cancel immediately for unpaid/suspended pending subscriptions; otherwise stop renewal and mark cancelled at period end.
        if (subscription.Status == MembershipStatus.Suspended)
        {
            subscription.Status = MembershipStatus.Expired;
            subscription.EndDate = DateTime.UtcNow.Date;
            subscription.AutoRenew = false;
            subscription.CancelledAt = DateTime.UtcNow;

            var unpaid = await _db.Invoices
                .Where(i => i.SubscriptionId == subscription.Id && (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue))
                .ToListAsync();
            foreach (var invoice in unpaid)
            {
                invoice.Status = InvoiceStatus.Cancelled;
            }
        }
        else
        {
            subscription.AutoRenew = false;
            subscription.CancelledAt = DateTime.UtcNow;
            // Keep Status Active until EndDate so the member can still use facilities this period.
        }

        var stillActive = await _db.Subscriptions.AnyAsync(s =>
            s.UserId == user.Id &&
            s.Id != subscription.Id &&
            s.Status == MembershipStatus.Active &&
            s.EndDate >= DateTime.UtcNow.Date);

        if (!stillActive && subscription.Status != MembershipStatus.Active)
        {
            user.MembershipStatus = MembershipStatus.Expired;
            await _userManager.UpdateAsync(user);
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            Action = "MembershipUnsubscribed",
            Entity = $"Subscription:{subscription.Id}",
            Details = $"{subscription.MembershipPlan?.Name} · ends {subscription.EndDate:yyyy-MM-dd}"
        });

        await _db.SaveChangesAsync();

        TempData["Success"] = subscription.Status == MembershipStatus.Active
            ? $"Unsubscribed. Auto-renewal is off. You can use facilities until {subscription.EndDate:dd MMM yyyy}."
            : "Membership cancelled.";

        return RedirectToAction(nameof(Manage));
    }
}
