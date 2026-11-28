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
        // Only Administrators see the operational stats dashboard; Staff keep the public hero.
        if (User.Identity?.IsAuthenticated == true && User.IsInRole(AppRoles.Administrator))
        {
            var dashboard = await _reportService.GetAdminHomeDashboardAsync();
            return View("AdminDashboard", dashboard);
        }

        var facilities = await _db.Facilities
            .Where(f => f.IsActive)
            .OrderBy(f => f.Name)
            .Take(8)
            .ToListAsync();

        return View(facilities);
    }

    // Legacy Contact & announcements page removed; contact lives in the site footer.
    public IActionResult Contact() => RedirectToAction(nameof(Index));

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
