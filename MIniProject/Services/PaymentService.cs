using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;

namespace MIniProject.Services;

public class PaymentService
{
    private readonly ApplicationDbContext _db;

    public PaymentService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<(bool Ok, string? Error)> PayInvoiceAsync(int invoiceId, string userId, string method, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Booking)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.UserId == userId, ct);

        if (invoice is null)
        {
            return (false, "Invoice not found.");
        }

        if (invoice.Status != InvoiceStatus.Unpaid && invoice.Status != InvoiceStatus.Overdue)
        {
            return (false, "Invoice is not payable.");
        }

        if (invoice.Booking is not null &&
            invoice.Booking.PaymentHoldExpiresAt is not null &&
            invoice.Booking.PaymentHoldExpiresAt < DateTime.UtcNow &&
            invoice.Booking.Status == BookingStatus.AwaitingPayment)
        {
            invoice.Booking.Status = BookingStatus.Cancelled;
            invoice.Booking.CancelledAt = DateTime.UtcNow;
            invoice.Status = InvoiceStatus.Cancelled;
            await _db.SaveChangesAsync(ct);
            return (false, "Payment hold expired. The booking was released.");
        }

        var payment = new PaymentTransaction
        {
            InvoiceId = invoice.Id,
            Amount = invoice.Amount,
            Method = method,
            Status = PaymentStatus.Completed,
            Reference = $"PAY-{Guid.NewGuid():N}"[..18].ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        _db.PaymentTransactions.Add(payment);
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;

        if (invoice.Booking is not null)
        {
            invoice.Booking.Status = BookingStatus.Confirmed;
            invoice.Booking.ConfirmedAt = DateTime.UtcNow;
            invoice.Booking.PaymentHoldExpiresAt = null;
        }

        if (invoice.SubscriptionId is not null)
        {
            var sub = await _db.Subscriptions.Include(s => s.User).FirstOrDefaultAsync(s => s.Id == invoice.SubscriptionId, ct);
            if (sub is not null)
            {
                sub.Status = MembershipStatus.Active;
                if (sub.User is not null)
                {
                    sub.User.MembershipStatus = MembershipStatus.Active;
                }
            }
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "PaymentCompleted",
            Entity = $"Invoice:{invoice.Id}",
            Details = $"{payment.Reference} via {method}"
        });

        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public decimal CalculateRefundAmount(Booking booking, IEnumerable<SystemSetting> settings, bool cancelledByClubhouse)
    {
        if (cancelledByClubhouse)
        {
            return booking.Amount;
        }

        var fullHours = int.Parse(settings.FirstOrDefault(s => s.Key == "RefundFullHours")?.Value ?? "48");
        var halfHours = int.Parse(settings.FirstOrDefault(s => s.Key == "RefundHalfHours")?.Value ?? "24");
        var hoursUntil = (booking.StartUtc - DateTime.UtcNow).TotalHours;

        if (hoursUntil >= fullHours)
        {
            return booking.Amount;
        }

        if (hoursUntil >= halfHours)
        {
            return Math.Round(booking.Amount * 0.5m, 2);
        }

        return 0m;
    }

    public async Task<(bool Ok, string? Error)> RequestRefundAsync(int bookingId, string userId, string reason, bool byClubhouse, CancellationToken ct = default)
    {
        var booking = await _db.Bookings
            .Include(b => b.Invoice)!.ThenInclude(i => i!.Payments)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null)
        {
            return (false, "Booking not found.");
        }

        if (!byClubhouse && booking.MemberId != userId)
        {
            return (false, "You can only cancel your own bookings.");
        }

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed)
        {
            return (false, "Booking cannot be cancelled.");
        }

        var settings = await _db.SystemSettings.ToListAsync(ct);
        var refundAmount = booking.Invoice?.Status == InvoiceStatus.Paid
            ? CalculateRefundAmount(booking, settings, byClubhouse)
            : 0m;

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;

        if (booking.Invoice is not null)
        {
            if (booking.Invoice.Status is InvoiceStatus.Unpaid or InvoiceStatus.Overdue)
            {
                booking.Invoice.Status = InvoiceStatus.Cancelled;
            }
            else if (refundAmount > 0)
            {
                _db.RefundRequests.Add(new RefundRequest
                {
                    InvoiceId = booking.Invoice.Id,
                    Amount = refundAmount,
                    Reason = reason,
                    Status = byClubhouse ? RefundStatus.Approved : RefundStatus.Pending
                });

                if (byClubhouse)
                {
                    booking.Invoice.Status = refundAmount >= booking.Invoice.Amount
                        ? InvoiceStatus.Refunded
                        : InvoiceStatus.PartiallyRefunded;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return (true, null);
    }
}
