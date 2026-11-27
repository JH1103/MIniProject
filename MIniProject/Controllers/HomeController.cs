using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;

namespace MIniProject.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ReportService _reportService;

    public HomeController(ApplicationDbContext db, ReportService reportService)
    {
        _db = db;
        _reportService = reportService;
    }

    public async Task<IActionResult> Index()
    {
        // Admin / Staff see an operational stats dashboard instead of the marketing hero.
        if (User.Identity?.IsAuthenticated == true &&
            (User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Staff)))
        {
            var dashboard = await _reportService.GetAdminHomeDashboardAsync();
            return View("AdminDashboard", dashboard);
        }

        var facilities = await _db.Facilities
            .Where(f => f.IsActive)
            .OrderBy(f => f.Name)
            .Take(8)
            .ToListAsync();

        var announcements = await _db.Announcements
            .Where(a => a.IsPublished)
            .OrderByDescending(a => a.PublishedAt)
            .Take(3)
            .ToListAsync();

        ViewBag.Announcements = announcements;
        return View(facilities);
    }

    public async Task<IActionResult> Contact()
    {
        ViewBag.Announcements = await _db.Announcements
            .Where(a => a.IsPublished)
            .OrderByDescending(a => a.PublishedAt)
            .ToListAsync();
        return View();
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
