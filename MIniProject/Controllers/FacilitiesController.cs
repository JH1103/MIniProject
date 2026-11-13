using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;

namespace MIniProject.Controllers;

public class FacilitiesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;

    public FacilitiesController(ApplicationDbContext db, BookingService bookingService)
    {
        _db = db;
        _bookingService = bookingService;
    }

    public async Task<IActionResult> Index(string? category, string? mode)
    {
        var selectedMode = NormalizeMode(mode);
        var query = _db.Facilities.Where(f => f.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(f => f.Category == category);
        }

        if (selectedMode == "instant")
        {
            query = query.Where(f => f.BookingMode == BookingMode.InstantConfirmation);
        }
        else if (selectedMode == "approval")
        {
            query = query.Where(f => f.BookingMode == BookingMode.ApprovalRequired);
        }

        ViewBag.Categories = await _db.Facilities.Where(f => f.IsActive).Select(f => f.Category).Distinct().OrderBy(c => c).ToListAsync();
        ViewBag.SelectedCategory = category;
        ViewBag.SelectedMode = selectedMode;
        return View(await query.OrderBy(f => f.Name).ToListAsync());
    }

    public async Task<IActionResult> Details(int id, DateTime? date)
    {
        var facility = await _db.Facilities
            .Include(f => f.InventoryItems)
            .FirstOrDefaultAsync(f => f.Id == id && f.IsActive);

        if (facility is null)
        {
            return NotFound();
        }

        var day = (date ?? DateTime.Today).Date;
        var dayStart = DateTime.SpecifyKind(day, DateTimeKind.Local).ToUniversalTime();
        var dayEnd = dayStart.AddDays(1);

        var bookings = await _db.Bookings
            .Where(b => b.FacilityId == id && b.StartUtc < dayEnd && b.EndUtc > dayStart)
            .ToListAsync();

        var blockOuts = await _db.FacilityBlockOuts
            .Where(b => b.FacilityId == id && b.StartUtc < dayEnd && b.EndUtc > dayStart)
            .ToListAsync();

        ViewBag.SelectedDate = day;
        ViewBag.Slots = _bookingService.GetDaySlots(facility, day, bookings, blockOuts);
        return View(facility);
    }

    private static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            "instant" or "instantconfirmation" or "instant-confirmation" => "instant",
            "approval" or "approvalrequired" or "approval-required" => "approval",
            _ => null
        };
    }
}
