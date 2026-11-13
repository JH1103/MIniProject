using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;

namespace MIniProject.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;

    public HomeController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
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
