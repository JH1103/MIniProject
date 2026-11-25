using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;

namespace MIniProject.Services;

public class BookingNotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IAppEmailSender _emailSender;
    private readonly ILogger<BookingNotificationService> _logger;

    public BookingNotificationService(
        ApplicationDbContext db,
        IAppEmailSender emailSender,
        ILogger<BookingNotificationService> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task SendConfirmationAsync(int bookingId, CancellationToken ct = default)
    {
        var booking = await _db.Bookings
            .Include(b => b.Facility)
            .Include(b => b.Member)
            .Include(b => b.Invoice)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null)
        {
            _logger.LogWarning("Booking confirmation email skipped: booking {Id} not found.", bookingId);
            return;
        }

        if (booking.Status != BookingStatus.Confirmed)
        {
            _logger.LogDebug("Booking confirmation email skipped: booking {Id} status is {Status}.", bookingId, booking.Status);
            return;
        }

        var member = booking.Member;
        if (member is null || string.IsNullOrWhiteSpace(member.Email))
        {
            _logger.LogWarning("Booking confirmation email skipped: member email missing for booking {Id}.", bookingId);
            return;
        }

        var startLocal = booking.StartUtc.ToLocalTime();
        var endLocal = booking.EndUtc.ToLocalTime();
        var facility = booking.Facility?.Name ?? "Facility";
        var amount = booking.Amount;
        var invoiceNo = booking.Invoice?.InvoiceNumber;

        var body = $"""
            Hello {member.FullName},

            Your facility booking is confirmed.

            Booking #: {booking.Id}
            Facility: {facility}
            Date: {startLocal:dddd, d MMMM yyyy}
            Time: {startLocal:h:mm tt} – {endLocal:h:mm tt}
            Amount: RM {amount:0.00}
            {(string.IsNullOrWhiteSpace(invoiceNo) ? "" : $"Invoice: {invoiceNo}\n")}
            Please arrive on time and bring your member pass for check-in.

            — Kota Permai Golf & Country Club
            """;

        try
        {
            var result = await _emailSender.SendAsync(
                member.Email,
                $"Booking confirmed — {facility}",
                body.TrimEnd() + "\n",
                ct);

            if (!result.Success)
            {
                _logger.LogError(
                    "Booking confirmation email failed for booking {Id} to {Email}: {Error}",
                    bookingId,
                    member.Email,
                    result.ErrorMessage);
                return;
            }

            _logger.LogInformation(
                "Booking confirmation email for booking {Id} delivered via {Mode} to {Email}.",
                bookingId,
                result.Mode,
                member.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send booking confirmation for booking {Id}.", bookingId);
        }
    }
}
