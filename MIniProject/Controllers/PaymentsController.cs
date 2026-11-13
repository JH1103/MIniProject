using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;
using MIniProject.ViewModels;
using QRCoder;

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
        var (result, invoice) = await TryGetAuthorizedInvoiceAsync(id);
        if (result is not null)
        {
            return result;
        }

        return View(new PaymentMethodViewModel { Invoice = invoice! });
    }

    [HttpGet]
    public async Task<IActionResult> PayCard(int id)
    {
        var (result, invoice) = await TryGetAuthorizedInvoiceAsync(id);
        if (result is not null)
        {
            return result;
        }

        if (invoice!.Status is not InvoiceStatus.Unpaid and not InvoiceStatus.Overdue)
        {
            return RedirectToAction(nameof(Receipt), new { id });
        }

        return View(new CardPaymentViewModel
        {
            InvoiceId = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            Amount = invoice.Amount,
            Description = invoice.Description,
            ExpiryMonth = DateTime.Today.Month,
            ExpiryYear = DateTime.Today.Year
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PayCard(CardPaymentViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var (result, invoice) = await TryGetAuthorizedInvoiceAsync(model.InvoiceId);
        if (result is not null)
        {
            return result;
        }

        model.InvoiceNumber = invoice!.InvoiceNumber;
        model.Amount = invoice.Amount;
        model.Description = invoice.Description;

        ValidateCardDetails(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var digits = Regex.Replace(model.CardNumber, @"\D", string.Empty);
        var last4 = digits[^4..];
        var method = $"Card ending {last4}";

        var (ok, error) = await _paymentService.PayInvoiceAsync(model.InvoiceId, user.Id, method);
        if (!ok)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Pay), new { id = model.InvoiceId });
        }

        TempData["Success"] = "Card payment successful. A confirmation receipt is available in your history.";
        return RedirectToAction(nameof(Receipt), new { id = model.InvoiceId });
    }

    [HttpGet]
    public async Task<IActionResult> PayEWallet(int id)
    {
        var (result, invoice) = await TryGetAuthorizedInvoiceAsync(id);
        if (result is not null)
        {
            return result;
        }

        if (invoice!.Status is not InvoiceStatus.Unpaid and not InvoiceStatus.Overdue)
        {
            return RedirectToAction(nameof(Receipt), new { id });
        }

        var reference = $"EW-{invoice.InvoiceNumber}-{invoice.Id}";
        var payload = $"kotapermai://pay?ref={reference}&amount={invoice.Amount:0.00}&currency=MYR&invoice={invoice.InvoiceNumber}";

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);

        return View(new EWalletPaymentViewModel
        {
            InvoiceId = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            Amount = invoice.Amount,
            Description = invoice.Description,
            PaymentReference = reference,
            QrBase64 = Convert.ToBase64String(qr.GetGraphic(8))
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmEWallet(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var (result, _) = await TryGetAuthorizedInvoiceAsync(id);
        if (result is not null)
        {
            return result;
        }

        var (ok, error) = await _paymentService.PayInvoiceAsync(id, user.Id, "E-Wallet");
        if (!ok)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(PayEWallet), new { id });
        }

        TempData["Success"] = "E-wallet payment successful. A confirmation receipt is available in your history.";
        return RedirectToAction(nameof(Receipt), new { id });
    }

    public async Task<IActionResult> Receipt(int id)
    {
        var (result, invoice) = await TryGetAuthorizedInvoiceAsync(id, includePayments: true);
        if (result is not null)
        {
            return result;
        }

        return View(invoice!);
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

    private async Task<(IActionResult? Result, Invoice? Invoice)> TryGetAuthorizedInvoiceAsync(int id, bool includePayments = false)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return (Challenge(), null);
        }

        IQueryable<Invoice> query = _db.Invoices
            .Include(i => i.Booking)!.ThenInclude(b => b!.Facility)
            .Include(i => i.Subscription)!.ThenInclude(s => s!.MembershipPlan);

        if (includePayments)
        {
            query = query.Include(i => i.Payments);
        }

        var invoice = await query.FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null)
        {
            return (NotFound(), null);
        }

        var isStaff = User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator);
        if (!isStaff && invoice.UserId != user.Id)
        {
            return (Forbid(), null);
        }

        return (null, invoice);
    }

    private void ValidateCardDetails(CardPaymentViewModel model)
    {
        var digits = Regex.Replace(model.CardNumber ?? string.Empty, @"\D", string.Empty);
        if (digits.Length != 16 || !digits.All(char.IsDigit))
        {
            ModelState.AddModelError(nameof(model.CardNumber), "Card number must be exactly 16 digits (grouped by 4).");
        }

        var currentYear = DateTime.Today.Year;
        var currentMonth = DateTime.Today.Month;
        if (model.ExpiryYear < currentYear || (model.ExpiryYear == currentYear && model.ExpiryMonth < currentMonth))
        {
            ModelState.AddModelError(nameof(model.ExpiryMonth), "Card has expired.");
        }

        if (model.ExpiryYear > currentYear + 20)
        {
            ModelState.AddModelError(nameof(model.ExpiryYear), "Enter a valid expiry year.");
        }
    }
}
