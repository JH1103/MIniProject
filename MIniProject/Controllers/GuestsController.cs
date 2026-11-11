using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using QRCoder;

namespace MIniProject.Controllers;

[Authorize]
public class GuestsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public GuestsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [Authorize(Roles = AppRoles.Member + "," + AppRoles.Administrator)]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var guests = await _db.Guests
            .Include(g => g.Passes)
            .Where(g => g.HostMemberId == user.Id)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync();

        return View(guests);
    }

    public async Task<IActionResult> Pass(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var pass = await _db.VisitorPasses
            .Include(p => p.Guest)
            .Include(p => p.Booking)!.ThenInclude(b => b!.Facility)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (pass is null)
        {
            return NotFound();
        }

        var isStaff = User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator);
        if (!isStaff && pass.Guest?.HostMemberId != user.Id)
        {
            return Forbid();
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(pass.PassCode, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);
        ViewBag.QrBase64 = Convert.ToBase64String(qr.GetGraphic(8));
        return View(pass);
    }
}
