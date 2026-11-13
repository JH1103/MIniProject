using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;

namespace MIniProject.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ReportService _reportService;

    public AdminController(ApplicationDbContext db, ReportService reportService)
    {
        _db = db;
        _reportService = reportService;
    }

    public async Task<IActionResult> Index(string? tab)
    {
        var selectedTab = string.IsNullOrWhiteSpace(tab) ? "facilities" : tab.Trim().ToLowerInvariant();
        ViewBag.SelectedTab = selectedTab;
        ViewBag.Stats = await _reportService.GetDashboardStatsAsync();
        ViewBag.Facilities = await _db.Facilities.Include(f => f.InventoryItems).OrderBy(f => f.Name).ToListAsync();
        ViewBag.Members = await _db.Users.OrderBy(u => u.FullName).Take(40).ToListAsync();
        ViewBag.Plans = await _db.MembershipPlans.Where(p => p.IsActive).OrderBy(p => p.AnnualFee).ToListAsync();
        ViewBag.Subscriptions = await _db.Subscriptions
            .Include(s => s.MembershipPlan)
            .Include(s => s.User)
            .OrderByDescending(s => s.CreatedAt)
            .Take(30)
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
            return View(new Facility());
        }

        var facility = await _db.Facilities.Include(f => f.InventoryItems).FirstOrDefaultAsync(f => f.Id == id);
        return facility is null ? NotFound() : View(facility);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditFacility(Facility model)
    {
        if (!ModelState.IsValid)
        {
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
    public async Task<IActionResult> AddInventory(int facilityId, string name, int quantity)
    {
        _db.InventoryItems.Add(new InventoryItem
        {
            FacilityId = facilityId,
            Name = name,
            Quantity = quantity
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Inventory item added.";
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
        var refund = await _db.RefundRequests.Include(r => r.Invoice).FirstOrDefaultAsync(r => r.Id == id);
        if (refund is null)
        {
            return NotFound();
        }

        refund.Status = approve ? RefundStatus.Completed : RefundStatus.Rejected;
        refund.ProcessedAt = DateTime.UtcNow;

        if (approve && refund.Invoice is not null)
        {
            refund.Invoice.Status = refund.Amount >= refund.Invoice.Amount
                ? InvoiceStatus.Refunded
                : InvoiceStatus.PartiallyRefunded;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = approve ? "Refund approved." : "Refund rejected.";
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
}
