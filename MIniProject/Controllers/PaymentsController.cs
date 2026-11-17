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

        ViewBag.Refunds = await _db.RefundRequests
            .Where(r => r.InvoiceId == id && r.Status == RefundStatus.Completed)
            .OrderByDescending(r => r.ProcessedAt)
            .ToListAsync();

        return View(invoice!);
    }

    public async Task<IActionResult> RefundReceipt(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var refund = await _db.RefundRequests
            .Include(r => r.Invoice!)
                .ThenInclude(i => i.User)
            .Include(r => r.Invoice!)
                .ThenInclude(i => i.Subscription!)
                .ThenInclude(s => s.MembershipPlan)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (refund?.Invoice is null)
        {
            return NotFound();
        }

        var isStaff = User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator);
        if (!isStaff && refund.Invoice.UserId != user.Id)
        {
            return Forbid();
        }

        if (refund.Status != RefundStatus.Completed)
        {
            TempData["Error"] = "A refund receipt is available only after an administrator approves the refund.";
            return RedirectToAction(nameof(History));
        }

        string approvedByName = "Administrator";
        string approvedByEmail = string.Empty;
        if (!string.IsNullOrWhiteSpace(refund.ProcessedByUserId))
        {
            var admin = await _userManager.FindByIdAsync(refund.ProcessedByUserId);
            if (admin is not null)
            {
                approvedByName = string.IsNullOrWhiteSpace(admin.FullName) ? (admin.Email ?? "Administrator") : admin.FullName;
                approvedByEmail = admin.Email ?? string.Empty;
            }
        }

        return View(new RefundReceiptViewModel
        {
            Refund = refund,
            Invoice = refund.Invoice,
            MemberName = refund.Invoice.User?.FullName ?? "Member",
            MemberEmail = refund.Invoice.User?.Email ?? string.Empty,
            ApprovedByName = approvedByName,
            ApprovedByEmail = approvedByEmail
        });
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
            .Include(i => i.Refunds)
            .Include(i => i.Booking!)
                .ThenInclude(b => b.Facility)
            .Include(i => i.Subscription!)
                .ThenInclude(s => s.MembershipPlan)
            .AsQueryable();

        if (!isStaff)
        {
            query = query.Where(i => i.UserId == user.Id);
        }

        var invoices = await query.OrderByDescending(i => i.IssuedAt).ToListAsync();
        var timeline = new List<PaymentHistoryItem>();

        foreach (var invoice in invoices)
        {
            foreach (var payment in invoice.Payments.Where(p => p.Status is PaymentStatus.Completed or PaymentStatus.Refunded))
            {
                timeline.Add(new PaymentHistoryItem
                {
                    Type = "Payment",
                    When = payment.CompletedAt ?? payment.CreatedAt,
                    Title = invoice.Description,
                    Detail = $"{invoice.InvoiceNumber} · {payment.Method}" +
                             (string.IsNullOrWhiteSpace(payment.Reference) ? string.Empty : $" · {payment.Reference}"),
                    Amount = payment.Amount,
                    Status = payment.Status.ToString(),
                    InvoiceId = invoice.Id,
                    IsCredit = false
                });
            }

            foreach (var refund in invoice.Refunds)
            {
                timeline.Add(new PaymentHistoryItem
                {
                    Type = "Refund",
                    When = refund.ProcessedAt ?? refund.RequestedAt,
                    Title = refund.Reason.StartsWith("Downgrade", StringComparison.OrdinalIgnoreCase)
                        ? "Membership downgrade refund"
                        : "Refund",
                    Detail = $"{invoice.InvoiceNumber} · {refund.Status}",
                    Amount = refund.Amount,
                    Status = refund.Status.ToString(),
                    InvoiceId = invoice.Id,
                    RefundId = refund.Id,
                    IsCredit = true
                });
            }
        }

        var unpaid = invoices
            .Where(i => i.Status is InvoiceStatus.Unpaid or InvoiceStatus.Overdue)
            .OrderBy(i => i.DueAt ?? i.IssuedAt)
            .ToList();

        return View(new PaymentHistoryViewModel
        {
            UnpaidInvoices = unpaid,
            Timeline = timeline.OrderByDescending(t => t.When).ToList(),
            TotalPaid = timeline.Where(t => t.Type == "Payment").Sum(t => t.Amount),
            TotalRefunded = timeline.Where(t => t.Type == "Refund" && t.Status == nameof(RefundStatus.Completed)).Sum(t => t.Amount),
            UnpaidCount = unpaid.Count
        });
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
