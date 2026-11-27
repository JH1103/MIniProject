using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;
using MIniProject.ViewModels;

namespace MIniProject.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ReportService _reportService;
    private readonly ReportPdfService _pdfService;

    public ReportsController(ApplicationDbContext db, ReportService reportService, ReportPdfService pdfService)
    {
        _db = db;
        _reportService = reportService;
        _pdfService = pdfService;
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

    /// <summary>
    /// Top-management report: which staff/admin helped members check in
    /// and which staff/admin marked equipment taken / picked up.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> StaffActivity(StaffActivityFilterViewModel filter)
    {
        var report = await _reportService.GetStaffActivityReportAsync(filter);
        return View(report);
    }

    [HttpGet]
    public async Task<IActionResult> ExportStaffActivity(StaffActivityFilterViewModel filter)
    {
        var report = await _reportService.GetStaffActivityReportAsync(filter);
        var sb = new StringBuilder();
        sb.AppendLine("DateUtc,ActionType,StaffUserId,StaffName,Member,Reference,Detail");
        foreach (var row in report.Rows)
        {
            sb.AppendLine(
                $"{row.OccurredAtUtc:u}," +
                $"\"{EscapeCsv(row.ActionType)}\"," +
                $"\"{EscapeCsv(row.StaffUserId)}\"," +
                $"\"{EscapeCsv(row.StaffName)}\"," +
                $"\"{EscapeCsv(row.MemberName)}\"," +
                $"\"{EscapeCsv(row.Reference)}\"," +
                $"\"{EscapeCsv(row.Detail)}\"");
        }

        return File(
            Encoding.UTF8.GetBytes(sb.ToString()),
            "text/csv",
            $"staff-activity-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string EscapeCsv(string? value) =>
        (value ?? string.Empty).Replace("\"", "\"\"");

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

    public async Task<IActionResult> ExportPdf()
    {
        var stats = await _reportService.GetDashboardStatsAsync();
        var clubName = await _db.SystemSettings
            .Where(s => s.Key == "ClubhouseName")
            .Select(s => s.Value)
            .FirstOrDefaultAsync() ?? "Kota Permai Golf & Country Club";

        var pdf = _pdfService.BuildMonthlyReport(stats, clubName);
        var fileName = $"kota-permai-report-{DateTime.UtcNow:yyyyMM}.pdf";
        return File(pdf, "application/pdf", fileName);
    }
}
