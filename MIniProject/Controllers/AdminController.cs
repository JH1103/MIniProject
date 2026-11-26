using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;
using MIniProject.ViewModels;

namespace MIniProject.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ReportService _reportService;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(ApplicationDbContext db, ReportService reportService, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _reportService = reportService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(string? tab)
    {
        var selectedTab = string.IsNullOrWhiteSpace(tab) ? "facilities" : tab.Trim().ToLowerInvariant();
        ViewBag.SelectedTab = selectedTab;
        ViewBag.Stats = await _reportService.GetDashboardStatsAsync();
        ViewBag.Facilities = await _db.Facilities.Include(f => f.InventoryItems).OrderBy(f => f.Name).ToListAsync();
        ViewBag.Members = await _db.Users.OrderBy(u => u.FullName).Take(40).ToListAsync();
        ViewBag.Plans = await _db.MembershipPlans.OrderByDescending(p => p.IsActive).ThenBy(p => p.AnnualFee).ToListAsync();
        ViewBag.Subscriptions = await _db.Subscriptions
            .Include(s => s.MembershipPlan)
            .Include(s => s.User)
            .OrderByDescending(s => s.CreatedAt)
            .Take(50)
            .ToListAsync();
        ViewBag.Bookings = await _db.Bookings
            .Include(b => b.Facility)
            .Include(b => b.Member)
            .OrderByDescending(b => b.CreatedAt)
            .Take(30)
            .ToListAsync();
        ViewBag.Invoices = await _db.Invoices
            .Include(i => i.User)
            .OrderByDescending(i => i.IssuedAt)
            .Take(30)
            .ToListAsync();
        ViewBag.Refunds = await _db.RefundRequests.Include(r => r.Invoice).OrderByDescending(r => r.RequestedAt).Take(20).ToListAsync();
        ViewBag.Settings = await _db.SystemSettings.OrderBy(s => s.Key).ToListAsync();
        ViewBag.AuditLogs = await _db.AuditLogs.OrderByDescending(a => a.CreatedAt).Take(40).ToListAsync();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> EditFacility(int? id)
    {
        if (id is null)
        {
            await PopulateFacilityCategoryOptionsAsync();
            return View(new Facility());
        }

        var facility = await _db.Facilities.Include(f => f.InventoryItems).FirstOrDefaultAsync(f => f.Id == id);
        if (facility is null)
        {
            return NotFound();
        }

        await PopulateFacilityCategoryOptionsAsync(facility.Category);
        return View(facility);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditFacility(Facility model)
    {
        model.Category = model.Category?.Trim() ?? string.Empty;
        var categoryOptions = await GetFacilityCategoryOptionsAsync(model.Category);
        if (string.IsNullOrWhiteSpace(model.Category) ||
            !categoryOptions.Contains(model.Category, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.Category), "Select a category from the list.");
        }
        else
        {
            // Normalize to the canonical casing from the option list.
            model.Category = categoryOptions.First(c =>
                string.Equals(c, model.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Categories = categoryOptions;
            if (model.Id > 0)
            {
                model.InventoryItems = await _db.InventoryItems
                    .Where(i => i.FacilityId == model.Id)
                    .ToListAsync();
            }

            return View(model);
        }

        if (model.Id == 0)
        {
            _db.Facilities.Add(model);
        }
        else
        {
            var existing = await _db.Facilities.FirstOrDefaultAsync(f => f.Id == model.Id);
            if (existing is null)
            {
                return NotFound();
            }

            existing.Name = model.Name;
            existing.Category = model.Category;
            existing.Description = model.Description;
            existing.ImageUrl = model.ImageUrl;
            existing.Capacity = model.Capacity;
            existing.HourlyRate = model.HourlyRate;
            existing.OpenTime = model.OpenTime;
            existing.CloseTime = model.CloseTime;
            existing.SlotDurationMinutes = model.SlotDurationMinutes;
            existing.BookingMode = model.BookingMode;
            existing.Status = model.Status;
            existing.IsActive = model.IsActive;
            existing.PaymentHoldMinutes = model.PaymentHoldMinutes;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Facility saved.";
        return RedirectToAction(nameof(Index), new { tab = "facilities" });
    }

    private async Task PopulateFacilityCategoryOptionsAsync(string? current = null)
    {
        ViewBag.Categories = await GetFacilityCategoryOptionsAsync(current);
    }

    private async Task<List<string>> GetFacilityCategoryOptionsAsync(string? current = null)
    {
        var existing = await _db.Facilities
            .Select(f => f.Category)
            .Where(c => c != null && c != "")
            .Distinct()
            .ToListAsync();
        return FacilityCategories.MergeWithExisting(existing, current);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFacilityStatus(int id, FacilityStatus status)
    {
        var facility = await _db.Facilities.FirstOrDefaultAsync(f => f.Id == id);
        if (facility is null)
        {
            return NotFound();
        }

        facility.Status = status;
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            Action = "FacilityStatusChanged",
            Entity = $"Facility:{facility.Id}",
            Details = $"{facility.Name} → {status}"
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"{facility.Name} marked as {status}.";
        return RedirectToAction(nameof(Index), new { tab = "facilities" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFacility(int id)
    {
        var facility = await _db.Facilities
            .Include(f => f.Bookings)
            .FirstOrDefaultAsync(f => f.Id == id);
        if (facility is null)
        {
            return NotFound();
        }

        var hasFutureBookings = facility.Bookings.Any(b =>
            b.Status != BookingStatus.Cancelled && b.EndUtc >= DateTime.UtcNow);

        if (hasFutureBookings)
        {
            facility.IsActive = false;
            facility.Status = FacilityStatus.Closed;
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                Action = "FacilitySoftDeleted",
                Entity = $"Facility:{facility.Id}",
                Details = $"{facility.Name} deactivated (has upcoming bookings)."
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"“{facility.Name}” has upcoming bookings, so it was deactivated instead of deleted.";
            return RedirectToAction(nameof(Index), new { tab = "facilities" });
        }

        _db.Facilities.Remove(facility);
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            Action = "FacilityDeleted",
            Entity = $"Facility:{id}",
            Details = facility.Name
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Facility “{facility.Name}” deleted.";
        return RedirectToAction(nameof(Index), new { tab = "facilities" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddInventory(int facilityId, string name, int quantity, InventoryCondition condition = InventoryCondition.Good, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(name) || quantity < 0)
        {
            TempData["Error"] = "Inventory name is required and quantity cannot be negative.";
            return RedirectToAction(nameof(EditFacility), new { id = facilityId });
        }

        _db.InventoryItems.Add(new InventoryItem
        {
            FacilityId = facilityId,
            Name = name.Trim(),
            Quantity = quantity,
            Condition = condition,
            Notes = notes
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Inventory item added.";
        return RedirectToAction(nameof(EditFacility), new { id = facilityId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateInventory(int id, string name, int quantity, InventoryCondition condition, string? notes)
    {
        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id);
        if (item is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(name) || quantity < 0)
        {
            TempData["Error"] = "Inventory name is required and quantity cannot be negative.";
            return RedirectToAction(nameof(EditFacility), new { id = item.FacilityId });
        }

        item.Name = name.Trim();
        item.Quantity = quantity;
        item.Condition = condition;
        item.Notes = notes;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Inventory “{item.Name}” updated.";
        return RedirectToAction(nameof(EditFacility), new { id = item.FacilityId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteInventory(int id)
    {
        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id);
        if (item is null)
        {
            return NotFound();
        }

        var facilityId = item.FacilityId;
        var hasActiveRentals = await _db.EquipmentRentals.AnyAsync(r =>
            r.InventoryItemId == id && r.Status == EquipmentRentalStatus.Active);
        if (hasActiveRentals)
        {
            TempData["Error"] = "Cannot delete equipment while active rentals exist. Mark rentals returned or invalid first.";
            return RedirectToAction(nameof(EditFacility), new { id = facilityId });
        }

        _db.InventoryItems.Remove(item);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Inventory “{item.Name}” deleted.";
        return RedirectToAction(nameof(EditFacility), new { id = facilityId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BlockOut(int facilityId, DateTime start, DateTime end, string reason)
    {
        _db.FacilityBlockOuts.Add(new FacilityBlockOut
        {
            FacilityId = facilityId,
            StartUtc = DateTime.SpecifyKind(start, DateTimeKind.Local).ToUniversalTime(),
            EndUtc = DateTime.SpecifyKind(end, DateTimeKind.Local).ToUniversalTime(),
            Reason = reason
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Block-out saved.";
        return RedirectToAction(nameof(EditFacility), new { id = facilityId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessRefund(int id, bool approve)
    {
        var refund = await _db.RefundRequests
            .Include(r => r.Invoice)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (refund is null)
        {
            return NotFound();
        }

        if (refund.Status is RefundStatus.Completed or RefundStatus.Rejected)
        {
            TempData["Error"] = "This refund request was already processed.";
            return RedirectToAction(nameof(Index), new { tab = "invoices" });
        }

        var adminId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        refund.Status = approve ? RefundStatus.Completed : RefundStatus.Rejected;
        refund.ProcessedAt = DateTime.UtcNow;
        refund.ProcessedByUserId = adminId;

        if (approve && refund.Invoice is not null)
        {
            refund.Invoice.Status = refund.Amount >= refund.Invoice.Amount
                ? InvoiceStatus.Refunded
                : InvoiceStatus.PartiallyRefunded;

            foreach (var payment in await _db.PaymentTransactions
                         .Where(p => p.InvoiceId == refund.InvoiceId && p.Status == PaymentStatus.Completed)
                         .ToListAsync())
            {
                if (refund.Amount >= payment.Amount)
                {
                    payment.Status = PaymentStatus.Refunded;
                }
            }
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            Action = approve ? "RefundApproved" : "RefundRejected",
            Entity = $"RefundRequest:{refund.Id}",
            Details = $"Invoice {refund.Invoice?.InvoiceNumber} · MYR {refund.Amount:N2} · {refund.Reason}"
        });

        await _db.SaveChangesAsync();
        TempData["Success"] = approve
            ? $"Refund approved (MYR {refund.Amount:N2}). A refund receipt is available for the member."
            : "Refund rejected.";
        return RedirectToAction(nameof(Index), new { tab = "invoices" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSetting(int id, string value)
    {
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Id == id);
        if (setting is null)
        {
            return NotFound();
        }

        setting.Value = value;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Setting updated.";
        return RedirectToAction(nameof(Index), new { tab = "audit" });
    }

    [HttpGet]
    public async Task<IActionResult> EditPlan(int? id)
    {
        if (id is null)
        {
            return View(new MembershipPlan
            {
                IsActive = true,
                AdvanceBookingDays = 14,
                MaxGuestsPerBooking = 4
            });
        }

        var plan = await _db.MembershipPlans.FirstOrDefaultAsync(p => p.Id == id);
        return plan is null ? NotFound() : View(plan);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPlan(MembershipPlan model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (model.AnnualFee < 0 || model.DiscountPercent < 0 || model.DiscountPercent > 100)
        {
            ModelState.AddModelError(string.Empty, "Fee must be ≥ 0 and discount must be between 0 and 100.");
            return View(model);
        }

        if (model.AdvanceBookingDays < 0 || model.MaxGuestsPerBooking < 0)
        {
            ModelState.AddModelError(string.Empty, "Advance booking days and guest limit cannot be negative.");
            return View(model);
        }

        if (model.Id == 0)
        {
            _db.MembershipPlans.Add(model);
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                Action = "MembershipPlanCreated",
                Entity = "MembershipPlan",
                Details = $"{model.Name} · MYR {model.AnnualFee:N2}"
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Membership plan “{model.Name}” created.";
        }
        else
        {
            var existing = await _db.MembershipPlans.FirstOrDefaultAsync(p => p.Id == model.Id);
            if (existing is null)
            {
                return NotFound();
            }

            existing.Name = model.Name.Trim();
            existing.Description = model.Description?.Trim() ?? string.Empty;
            existing.AnnualFee = model.AnnualFee;
            existing.AdvanceBookingDays = model.AdvanceBookingDays;
            existing.MaxGuestsPerBooking = model.MaxGuestsPerBooking;
            existing.DiscountPercent = model.DiscountPercent;
            existing.IsActive = model.IsActive;

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                Action = "MembershipPlanUpdated",
                Entity = $"MembershipPlan:{existing.Id}",
                Details = $"{existing.Name} · MYR {existing.AnnualFee:N2} · active={existing.IsActive}"
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Membership plan “{existing.Name}” updated.";
        }

        return RedirectToAction(nameof(Index), new { tab = "membership" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePlan(int id)
    {
        var plan = await _db.MembershipPlans
            .Include(p => p.Subscriptions)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (plan is null)
        {
            return NotFound();
        }

        var hasSubscriptions = plan.Subscriptions.Any();
        if (hasSubscriptions)
        {
            // Soft-delete so historical subscriptions remain valid.
            plan.IsActive = false;
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                Action = "MembershipPlanDeactivated",
                Entity = $"MembershipPlan:{plan.Id}",
                Details = $"{plan.Name} deactivated (has existing subscriptions)."
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"“{plan.Name}” has subscriptions, so it was deactivated instead of deleted.";
        }
        else
        {
            _db.MembershipPlans.Remove(plan);
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                Action = "MembershipPlanDeleted",
                Entity = $"MembershipPlan:{id}",
                Details = plan.Name
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Membership plan “{plan.Name}” deleted.";
        }

        return RedirectToAction(nameof(Index), new { tab = "membership" });
    }

    [HttpGet]
    public IActionResult CreateMember()
    {
        return View(new AdminMemberFormViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMember(AdminMemberFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(model.Password), "Password is required for new members.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            FullName = model.FullName.Trim(),
            PhoneNumber = model.PhoneNumber,
            IdentificationNumber = model.IdentificationNumber,
            Address = model.Address,
            MembershipStatus = model.MembershipStatus,
            EmailConfirmed = true,
            PdpaConsent = true,
            PdpaConsentAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        await _userManager.AddToRoleAsync(user, AppRoles.Member);
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            Action = "MemberCreated",
            Entity = $"User:{user.Id}",
            Details = user.Email
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Member “{user.FullName}” created.";
        return RedirectToAction(nameof(Index), new { tab = "membership" });
    }

    [HttpGet]
    public async Task<IActionResult> EditMember(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        return View(new AdminMemberFormViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            IdentificationNumber = user.IdentificationNumber,
            Address = user.Address,
            MembershipStatus = user.MembershipStatus
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditMember(AdminMemberFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByIdAsync(model.Id);
        if (user is null)
        {
            return NotFound();
        }

        user.FullName = model.FullName.Trim();
        user.PhoneNumber = model.PhoneNumber;
        user.IdentificationNumber = model.IdentificationNumber;
        user.Address = model.Address;
        user.MembershipStatus = model.MembershipStatus;

        if (!string.Equals(user.Email, model.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var setEmail = await _userManager.SetEmailAsync(user, model.Email.Trim());
            if (!setEmail.Succeeded)
            {
                foreach (var error in setEmail.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            await _userManager.SetUserNameAsync(user, model.Email.Trim());
        }

        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await _userManager.ResetPasswordAsync(user, token, model.Password);
            if (!reset.Succeeded)
            {
                foreach (var error in reset.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }
        }

        await _userManager.UpdateAsync(user);
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            Action = "MemberUpdated",
            Entity = $"User:{user.Id}",
            Details = user.Email
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Member “{user.FullName}” updated.";
        return RedirectToAction(nameof(Index), new { tab = "membership" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMember(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (await _userManager.IsInRoleAsync(user, AppRoles.Administrator) ||
            await _userManager.IsInRoleAsync(user, AppRoles.Staff))
        {
            TempData["Error"] = "Staff and administrator accounts cannot be deleted here.";
            return RedirectToAction(nameof(Index), new { tab = "membership" });
        }

        var openSubs = await _db.Subscriptions
            .Where(s => s.UserId == id && s.Status == MembershipStatus.Active && s.CancelledAt == null && s.EndDate >= DateTime.UtcNow.Date)
            .ToListAsync();
        foreach (var sub in openSubs)
        {
            sub.CancelledAt = DateTime.UtcNow;
            sub.AutoRenew = false;
            sub.Status = MembershipStatus.Suspended;
        }

        user.MembershipStatus = MembershipStatus.Suspended;
        await _userManager.UpdateAsync(user);

        // Soft-delete: deactivate rather than hard-remove identity history.
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        await _userManager.UpdateAsync(user);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            Action = "MemberDeactivated",
            Entity = $"User:{user.Id}",
            Details = user.Email
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Member “{user.FullName}” deactivated and locked out.";
        return RedirectToAction(nameof(Index), new { tab = "membership" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelSubscription(int id)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.MembershipPlan)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null)
        {
            return NotFound();
        }

        sub.CancelledAt = DateTime.UtcNow;
        sub.AutoRenew = false;
        if (sub.EndDate < DateTime.UtcNow.Date)
        {
            sub.Status = MembershipStatus.Expired;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            Action = "SubscriptionCancelled",
            Entity = $"Subscription:{sub.Id}",
            Details = $"{sub.User?.Email} · {sub.MembershipPlan?.Name}"
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Subscription cancelled.";
        return RedirectToAction(nameof(Index), new { tab = "membership" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSubscription(int id)
    {
        var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null)
        {
            return NotFound();
        }

        // Only remove clearly inactive/invalid subscription rows.
        var isInvalid = sub.Status != MembershipStatus.Active
                        || sub.CancelledAt is not null
                        || sub.EndDate < DateTime.UtcNow.Date;
        if (!isInvalid)
        {
            TempData["Error"] = "Active subscriptions must be cancelled before delete.";
            return RedirectToAction(nameof(Index), new { tab = "membership" });
        }

        _db.Subscriptions.Remove(sub);
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            Action = "SubscriptionDeleted",
            Entity = $"Subscription:{id}",
            Details = "Removed invalid/inactive subscription record"
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Invalid subscription record deleted.";
        return RedirectToAction(nameof(Index), new { tab = "membership" });
    }
}
