using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
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
    private readonly UserManager<ApplicationUser> _userManager;

    public HomeController(
        ApplicationDbContext db,
        ReportService reportService,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _reportService = reportService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        // Only Administrators see the operational stats dashboard.
        if (User.Identity?.IsAuthenticated == true && User.IsInRole(AppRoles.Administrator))
        {
            var dashboard = await _reportService.GetAdminHomeDashboardAsync();
            return View("AdminDashboard", dashboard);
        }

        // Signed-in Member / Staff get a personal welcome hero instead of the public marketing page.
        if (User.Identity?.IsAuthenticated == true &&
            (User.IsInRole(AppRoles.Member) || User.IsInRole(AppRoles.Staff)))
        {
            var user = await _userManager.GetUserAsync(User);
            var displayName = !string.IsNullOrWhiteSpace(user?.FullName)
                ? user!.FullName.Trim()
                : (user?.Email ?? User.Identity?.Name ?? "there");

            ViewBag.WelcomeName = displayName;
            // Prefer Staff CTAs when the account is staff (staff may also hold Member).
            ViewBag.WelcomeRole = User.IsInRole(AppRoles.Staff) ? AppRoles.Staff : AppRoles.Member;
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
