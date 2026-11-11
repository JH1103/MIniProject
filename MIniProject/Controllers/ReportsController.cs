using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;

namespace MIniProject.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ReportService _reportService;

    public ReportsController(ApplicationDbContext db, ReportService reportService)
    {
        _db = db;
        _reportService = reportService;
    }

    public async Task<IActionResult> Index()
    {
        var stats = await _reportService.GetDashboardStatsAsync();
        ViewBag.RecentBookings = await _db.Bookings
            .Include(b => b.Facility)
            .Include(b => b.Member)
            .OrderByDescending(b => b.CreatedAt)
            .Take(20)
            .ToListAsync();
        ViewBag.Payments = await _db.PaymentTransactions
            .Include(p => p.Invoice)
            .OrderByDescending(p => p.CreatedAt)
            .Take(20)
            .ToListAsync();
        return View(stats);
    }

    public async Task<IActionResult> ExportBookings()
    {
        var rows = await _db.Bookings
            .Include(b => b.Facility)
            .Include(b => b.Member)
            .OrderByDescending(b => b.StartUtc)
            .Take(500)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Id,Facility,Member,StartUtc,EndUtc,Status,Amount");
        foreach (var b in rows)
        {
            sb.AppendLine($"{b.Id},\"{b.Facility?.Name}\",\"{b.Member?.FullName}\",{b.StartUtc:u},{b.EndUtc:u},{b.Status},{b.Amount}");
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"bookings-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
