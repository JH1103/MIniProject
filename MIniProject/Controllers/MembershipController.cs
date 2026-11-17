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

        // Personal plan browsing/subscribe is for members only.
        if (User.Identity?.IsAuthenticated == true &&
            !User.IsInRole(AppRoles.Administrator) &&
            !User.IsInRole(AppRoles.Staff))
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is not null)
            {
                ViewBag.CurrentSubscription = await _bookingService.GetActiveSubscriptionAsync(user.Id);
            }
        }

        return View(plans);
    }

    [Authorize(Roles = AppRoles.Member)]
    [HttpGet]
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

        var current = await _bookingService.GetActiveSubscriptionAsync(user.Id);
        var model = BuildSubscribeModel(plan, current);
        if (current is not null && current.MembershipPlanId == plan.Id)
        {
            TempData["Error"] = "You are already on this membership plan.";
            return RedirectToAction(nameof(Manage));
        }

        return View(model);
    }

    [Authorize(Roles = AppRoles.Member)]
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

        var current = await _bookingService.GetActiveSubscriptionAsync(user.Id);
        model = BuildSubscribeModel(plan, current, model.AutoRenew);

        if (!ModelState.IsValid)
        {
            return View("Subscribe", model);
        }

        if (current is not null && current.MembershipPlanId == plan.Id)
        {
            TempData["Error"] = "You are already on this membership plan.";
            return RedirectToAction(nameof(Manage));
        }

        // Cancel unfinished pending plan-change invoices for this member.
        var pendingChangeInvoices = await _db.Invoices
            .Where(i => i.UserId == user.Id &&
                        i.SubscriptionId != null &&
                        (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue) &&
                        i.BookingId == null)
            .ToListAsync();
        foreach (var pending in pendingChangeInvoices)
        {
            pending.Status = InvoiceStatus.Cancelled;
        }

        var previous = await _db.Subscriptions
            .Include(s => s.MembershipPlan)
            .Where(s => s.UserId == user.Id &&
                        (s.Status == MembershipStatus.Active || s.Status == MembershipStatus.Suspended))
            .ToListAsync();

        var keptEndDate = current?.EndDate.Date ?? DateTime.UtcNow.Date.AddYears(1);
        var isChange = current?.MembershipPlan is not null;
        var isUpgrade = isChange && plan.AnnualFee > (current!.MembershipPlan!.AnnualFee);
        var isDowngrade = isChange && plan.AnnualFee < current!.MembershipPlan!.AnnualFee;

        // Only one active plan: deactivate every previous plan immediately.
        foreach (var old in previous)
        {
            MembershipChangeHelper.DeactivateSubscription(old);
        }

        if (isDowngrade)
        {
            var difference = current!.MembershipPlan!.AnnualFee - plan.AnnualFee;
            var refundCredit = Math.Round(difference * MembershipChangeHelper.DowngradeRefundRate, 2);

            var subscription = new Subscription
            {
                UserId = user.Id,
                MembershipPlanId = plan.Id,
                StartDate = DateTime.UtcNow.Date,
                EndDate = keptEndDate,
                Status = MembershipStatus.Active,
                AutoRenew = model.AutoRenew,
                CancelledAt = null
            };
            _db.Subscriptions.Add(subscription);
            user.MembershipStatus = MembershipStatus.Active;
            await _db.SaveChangesAsync();

            var paidInvoice = await _db.Invoices
                .Where(i => i.UserId == user.Id &&
                            i.SubscriptionId == current.Id &&
                            i.Status == InvoiceStatus.Paid)
                .OrderByDescending(i => i.PaidAt)
                .FirstOrDefaultAsync();

            if (paidInvoice is null)
            {
                paidInvoice = await _db.Invoices
                    .Where(i => i.UserId == user.Id &&
                                i.SubscriptionId != null &&
                                i.Status == InvoiceStatus.Paid)
                    .OrderByDescending(i => i.PaidAt)
                    .FirstOrDefaultAsync();
            }

            if (paidInvoice is not null && refundCredit > 0)
            {
                _db.RefundRequests.Add(new RefundRequest
                {
                    InvoiceId = paidInvoice.Id,
                    Amount = refundCredit,
                    Status = RefundStatus.Pending,
                    Reason = $"Downgrade {current.MembershipPlan.Name} → {plan.Name}: 75% of MYR {difference:N2} difference. Expiry kept {keptEndDate:dd MMM yyyy}."
                });
            }

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = user.Id,
                Action = "MembershipDowngraded",
                Entity = $"Subscription:{subscription.Id}",
                Details = $"{current!.MembershipPlan!.Name} → {plan.Name} · credit MYR {refundCredit:N2} (75%) · ends {keptEndDate:yyyy-MM-dd}"
            });
            await _db.SaveChangesAsync();

            TempData["Success"] = refundCredit > 0
                ? $"Downgraded to {plan.Name}. Expiry stays {keptEndDate:dd MMM yyyy}. A 75% difference credit of MYR {refundCredit:N2} was submitted for refund processing."
                : $"Downgraded to {plan.Name}. Expiry stays {keptEndDate:dd MMM yyyy}.";
            return RedirectToAction(nameof(Manage));
        }

        var amountDue = isUpgrade
            ? Math.Round(plan.AnnualFee - current!.MembershipPlan!.AnnualFee, 2)
            : plan.AnnualFee;

        var subscriptionPending = new Subscription
        {
            UserId = user.Id,
            MembershipPlanId = plan.Id,
            StartDate = DateTime.UtcNow.Date,
            EndDate = isChange ? keptEndDate : DateTime.UtcNow.Date.AddYears(1),
            Status = MembershipStatus.Suspended,
            AutoRenew = model.AutoRenew
        };
        _db.Subscriptions.Add(subscriptionPending);
        await _db.SaveChangesAsync();

        var renewNote = model.AutoRenew ? " · auto-renewal on" : " · auto-renewal off";
        string description;
        if (isUpgrade)
        {
            description = $"Upgrade {current!.MembershipPlan!.Name} → {plan.Name}: pay difference MYR {amountDue:N2} (expiry kept {keptEndDate:dd MMM yyyy}){renewNote}";
        }
        else
        {
            description = $"{plan.Name} membership (1 year){renewNote}";
        }

        var invoice = new Invoice
        {
            InvoiceNumber = $"MEM-{DateTime.UtcNow:yyyyMMdd}-{subscriptionPending.Id:D5}",
            UserId = user.Id,
            SubscriptionId = subscriptionPending.Id,
            Amount = Math.Max(0, amountDue),
            Status = InvoiceStatus.Unpaid,
            Description = description,
            DueAt = DateTime.UtcNow.AddDays(3)
        };
        _db.Invoices.Add(invoice);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            Action = isUpgrade ? "MembershipUpgradeStarted" : "MembershipSubscribeStarted",
            Entity = $"Subscription:{subscriptionPending.Id}",
            Details = description
        });
        await _db.SaveChangesAsync();

        if (invoice.Amount == 0)
        {
            // Edge case: same fee switch — activate without payment.
            subscriptionPending.Status = MembershipStatus.Active;
            user.MembershipStatus = MembershipStatus.Active;
            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Switched to {plan.Name}. Expiry stays {subscriptionPending.EndDate:dd MMM yyyy}.";
            return RedirectToAction(nameof(Manage));
        }

        TempData["Success"] = isUpgrade
            ? $"Upgrade to {plan.Name}: pay only the difference (MYR {amountDue:N2}). Expiry stays {keptEndDate:dd MMM yyyy}."
            : (model.AutoRenew
                ? "Auto-renewal enabled. Complete payment to activate your membership."
                : "Auto-renewal disabled. Complete payment to activate your membership for one year.");

        return RedirectToAction("Pay", "Payments", new { id = invoice.Id });
    }

    private static SubscribeConfirmViewModel BuildSubscribeModel(MembershipPlan plan, Subscription? current, bool autoRenew = true)
    {
        var model = new SubscribeConfirmViewModel
        {
            PlanId = plan.Id,
            PlanName = plan.Name,
            Description = plan.Description,
            AnnualFee = plan.AnnualFee,
            AdvanceBookingDays = plan.AdvanceBookingDays,
            MaxGuestsPerBooking = plan.MaxGuestsPerBooking,
            DiscountPercent = plan.DiscountPercent,
            AutoRenew = autoRenew
        };

        if (current?.MembershipPlan is null)
        {
            model.ChangeType = "New";
            model.AmountDue = plan.AnnualFee;
            model.IsPlanChange = false;
            return model;
        }

        model.IsPlanChange = true;
        model.CurrentPlanName = current.MembershipPlan.Name;
        model.CurrentAnnualFee = current.MembershipPlan.AnnualFee;
        model.KeptEndDate = current.EndDate.Date;
        model.PriceDifference = Math.Abs(plan.AnnualFee - current.MembershipPlan.AnnualFee);

        if (plan.AnnualFee > current.MembershipPlan.AnnualFee)
        {
            model.ChangeType = "Upgrade";
            model.AmountDue = Math.Round(plan.AnnualFee - current.MembershipPlan.AnnualFee, 2);
            model.RefundCredit = 0;
        }
        else if (plan.AnnualFee < current.MembershipPlan.AnnualFee)
        {
            model.ChangeType = "Downgrade";
            model.AmountDue = 0;
            model.RefundCredit = Math.Round(model.PriceDifference * MembershipChangeHelper.DowngradeRefundRate, 2);
        }
        else
        {
            model.ChangeType = "Switch";
            model.AmountDue = 0;
            model.RefundCredit = 0;
        }

        return model;
    }

    [Authorize(Roles = AppRoles.Member)]
    [HttpGet]
    public async Task<IActionResult> Manage()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Staff))
        {
            TempData["Error"] = "Staff and administrator work accounts do not hold personal membership plans.";
            return RedirectToAction("Index", "Home");
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

    [Authorize(Roles = AppRoles.Member)]
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

    [Authorize(Roles = AppRoles.Member)]
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
