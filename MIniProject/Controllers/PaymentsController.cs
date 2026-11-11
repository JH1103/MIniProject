using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;

namespace MIniProject.Controllers;

[Authorize]
public class PaymentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly PaymentService _paymentService;
    private readonly UserManager<ApplicationUser> _userManager;

    public PaymentsController(ApplicationDbContext db, PaymentService paymentService, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _paymentService = paymentService;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Pay(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var invoice = await _db.Invoices
            .Include(i => i.Booking)!.ThenInclude(b => b!.Facility)
            .Include(i => i.Subscription)!.ThenInclude(s => s!.MembershipPlan)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice is null)
        {
            return NotFound();
        }

        var isStaff = User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator);
        if (!isStaff && invoice.UserId != user.Id)
        {
            return Forbid();
        }

        return View(invoice);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(int id, string method)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var (ok, error) = await _paymentService.PayInvoiceAsync(id, user.Id, method);
        if (!ok)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Pay), new { id });
        }

        TempData["Success"] = "Payment successful. A confirmation receipt is available in your history.";
        return RedirectToAction(nameof(Receipt), new { id });
    }

    public async Task<IActionResult> Receipt(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var invoice = await _db.Invoices
            .Include(i => i.Payments)
            .Include(i => i.Booking)!.ThenInclude(b => b!.Facility)
            .Include(i => i.Subscription)!.ThenInclude(s => s!.MembershipPlan)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice is null)
        {
            return NotFound();
        }

        var isStaff = User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator);
        if (!isStaff && invoice.UserId != user.Id)
        {
            return Forbid();
        }

        return View(invoice);
    }

    public async Task<IActionResult> History()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var isStaff = User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator);
        var query = _db.Invoices
            .Include(i => i.Payments)
            .Include(i => i.Booking)
            .AsQueryable();

        if (!isStaff)
        {
            query = query.Where(i => i.UserId == user.Id);
        }

        var invoices = await query.OrderByDescending(i => i.IssuedAt).ToListAsync();
        return View(invoices);
    }
}
