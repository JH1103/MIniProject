using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;

namespace MIniProject.Services;

public class BookingService
{
    private readonly ApplicationDbContext _db;

    public BookingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task ReleaseExpiredHoldsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.Bookings
            .Where(b =>
                (b.Status == BookingStatus.AwaitingPayment || b.Status == BookingStatus.Pending) &&
                b.PaymentHoldExpiresAt != null &&
                b.PaymentHoldExpiresAt < now)
            .ToListAsync(ct);

        foreach (var booking in expired)
        {
            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = now;
            if (booking.InvoiceIdCompatible())
            {
                // invoice linked via navigation; mark unpaid holds cancelled below
            }
        }

        var unpaidInvoices = await _db.Invoices
            .Where(i => i.Status == InvoiceStatus.Unpaid && i.BookingId != null)
            .Include(i => i.Booking)
            .Where(i => i.Booking != null && i.Booking.Status == BookingStatus.Cancelled)
            .ToListAsync(ct);

        foreach (var invoice in unpaidInvoices)
        {
            invoice.Status = InvoiceStatus.Cancelled;
        }

        if (expired.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<Subscription?> GetActiveSubscriptionAsync(string userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.Date;
        return await _db.Subscriptions
            .Include(s => s.MembershipPlan)
            .Where(s => s.UserId == userId &&
                        s.Status == MembershipStatus.Active &&
                        s.StartDate <= now &&
                        s.EndDate >= now)
            .OrderBy(s => s.CancelledAt == null ? 0 : 1)
            .ThenByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.EndDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<(bool Ok, string? Error, Booking? Booking)> CreateBookingAsync(
        string memberId,
        int facilityId,
        DateTime startLocal,
        DateTime endLocal,
        IEnumerable<(string FullName, string? IdNumber, string? Phone)> guests,
        CancellationToken ct = default)
    {
        await ReleaseExpiredHoldsAsync(ct);

        var facility = await _db.Facilities.FirstOrDefaultAsync(f => f.Id == facilityId && f.IsActive, ct);
        if (facility is null)
        {
            return (false, "Facility not found.", null);
        }

        if (facility.Status != FacilityStatus.Available)
        {
            return (false, "Facility is currently unavailable.", null);
        }

        var subscription = await GetActiveSubscriptionAsync(memberId, ct);
        if (subscription?.MembershipPlan is null)
        {
            return (false, "An active membership is required to book facilities.", null);
        }

        var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();
        var endUtc = DateTime.SpecifyKind(endLocal, DateTimeKind.Local).ToUniversalTime();

        if (endUtc <= startUtc)
        {
            return (false, "End time must be after start time.", null);
        }

        if (startUtc < DateTime.UtcNow)
        {
            return (false, "Cannot book a slot in the past.", null);
        }

        var maxAdvance = DateTime.UtcNow.Date.AddDays(subscription.MembershipPlan.AdvanceBookingDays);
        if (startUtc.Date > maxAdvance)
        {
            return (false, $"Your plan allows bookings up to {subscription.MembershipPlan.AdvanceBookingDays} days in advance.", null);
        }

        var localDate = startLocal.Date;
        var open = localDate.Add(facility.OpenTime);
        var close = localDate.Add(facility.CloseTime);
        if (startLocal < open || endLocal > close)
        {
            return (false, "Selected time is outside facility operating hours.", null);
        }

        var guestList = guests.Where(g => !string.IsNullOrWhiteSpace(g.FullName)).ToList();
        if (guestList.Count > subscription.MembershipPlan.MaxGuestsPerBooking)
        {
            return (false, $"Your plan allows up to {subscription.MembershipPlan.MaxGuestsPerBooking} guests per booking.", null);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var blocked = await _db.FacilityBlockOuts.AnyAsync(b =>
            b.FacilityId == facilityId &&
            b.StartUtc < endUtc &&
            b.EndUtc > startUtc, ct);

        if (blocked)
        {
            return (false, "Facility is blocked for maintenance or a private event.", null);
        }

        var conflict = await _db.Bookings.AnyAsync(b =>
            b.FacilityId == facilityId &&
            b.Status != BookingStatus.Cancelled &&
            b.StartUtc < endUtc &&
            b.EndUtc > startUtc, ct);

        if (conflict)
        {
            return (false, "This time slot is no longer available.", null);
        }

        var hours = (decimal)(endUtc - startUtc).TotalHours;
        var baseAmount = Math.Round(facility.HourlyRate * hours, 2);
        var discount = Math.Round(baseAmount * (subscription.MembershipPlan.DiscountPercent / 100m), 2);
        var amount = Math.Max(0, baseAmount - discount);

        var booking = new Booking
        {
            MemberId = memberId,
            FacilityId = facilityId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Amount = amount,
            CreatedAt = DateTime.UtcNow,
            PaymentHoldExpiresAt = DateTime.UtcNow.AddMinutes(facility.PaymentHoldMinutes),
            Status = facility.BookingMode == BookingMode.ApprovalRequired
                ? BookingStatus.Pending
                : BookingStatus.AwaitingPayment
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync(ct);

        foreach (var g in guestList)
        {
            var guest = new Guest
            {
                FullName = g.FullName.Trim(),
                IdentificationNumber = g.IdNumber,
                Phone = g.Phone,
                HostMemberId = memberId
            };
            _db.Guests.Add(guest);
            await _db.SaveChangesAsync(ct);

            _db.BookingGuests.Add(new BookingGuest
            {
                BookingId = booking.Id,
                GuestId = guest.Id
            });

            _db.VisitorPasses.Add(new VisitorPass
            {
                GuestId = guest.Id,
                BookingId = booking.Id,
                PassCode = $"GC-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
                // Valid from the start of the booking day so staff can check guests in on arrival.
                ValidFrom = DateTime.SpecifyKind(startLocal.Date, DateTimeKind.Local).ToUniversalTime(),
                ValidTo = endUtc.AddHours(1)
            });
        }

        if (facility.BookingMode == BookingMode.InstantConfirmation)
        {
            var invoice = new Invoice
            {
                InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{booking.Id:D5}",
                UserId = memberId,
                BookingId = booking.Id,
                Amount = amount,
                Status = amount == 0 ? InvoiceStatus.Paid : InvoiceStatus.Unpaid,
                Description = $"Booking for {facility.Name}",
                DueAt = booking.PaymentHoldExpiresAt,
                PaidAt = amount == 0 ? DateTime.UtcNow : null
            };
            _db.Invoices.Add(invoice);

            if (amount == 0)
            {
                booking.Status = BookingStatus.Confirmed;
                booking.ConfirmedAt = DateTime.UtcNow;
            }
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = memberId,
            Action = "BookingCreated",
            Entity = $"Booking:{booking.Id}",
            Details = $"{facility.Name} {startUtc:u} - {endUtc:u}"
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (true, null, booking);
    }

    public async Task<(bool Ok, string? Error)> ApproveBookingAsync(int bookingId, string staffUserId, CancellationToken ct = default)
    {
        var booking = await _db.Bookings.Include(b => b.Facility).FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null)
        {
            return (false, "Booking not found.");
        }

        if (booking.Status != BookingStatus.Pending)
        {
            return (false, "Only pending bookings can be approved.");
        }

        booking.Status = BookingStatus.AwaitingPayment;
        booking.ApprovedByUserId = staffUserId;
        booking.ApprovedAt = DateTime.UtcNow;
        booking.PaymentHoldExpiresAt = DateTime.UtcNow.AddMinutes(booking.Facility?.PaymentHoldMinutes ?? 15);

        if (!await _db.Invoices.AnyAsync(i => i.BookingId == booking.Id, ct))
        {
            _db.Invoices.Add(new Invoice
            {
                InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{booking.Id:D5}",
                UserId = booking.MemberId,
                BookingId = booking.Id,
                Amount = booking.Amount,
                Status = booking.Amount == 0 ? InvoiceStatus.Paid : InvoiceStatus.Unpaid,
                Description = $"Booking for {booking.Facility?.Name}",
                DueAt = booking.PaymentHoldExpiresAt,
                PaidAt = booking.Amount == 0 ? DateTime.UtcNow : null
            });

            if (booking.Amount == 0)
            {
                booking.Status = BookingStatus.Confirmed;
                booking.ConfirmedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public List<(DateTime Start, DateTime End, bool Available)> GetDaySlots(Facility facility, DateTime day, IEnumerable<Booking> bookings, IEnumerable<FacilityBlockOut> blockOuts)
    {
        var slots = new List<(DateTime Start, DateTime End, bool Available)>();
        var cursor = day.Date.Add(facility.OpenTime);
        var close = day.Date.Add(facility.CloseTime);
        var duration = TimeSpan.FromMinutes(facility.SlotDurationMinutes);

        while (cursor + duration <= close)
        {
            var end = cursor + duration;
            var startUtc = DateTime.SpecifyKind(cursor, DateTimeKind.Local).ToUniversalTime();
            var endUtc = DateTime.SpecifyKind(end, DateTimeKind.Local).ToUniversalTime();

            var taken = bookings.Any(b =>
                b.Status != BookingStatus.Cancelled &&
                b.StartUtc < endUtc &&
                b.EndUtc > startUtc);

            var blocked = blockOuts.Any(b => b.StartUtc < endUtc && b.EndUtc > startUtc);
            var past = startUtc < DateTime.UtcNow;

            slots.Add((cursor, end, !taken && !blocked && !past && facility.Status == FacilityStatus.Available));
            cursor = end;
        }

        return slots;
    }
}

internal static class BookingInvoiceExtensions
{
    public static bool InvoiceIdCompatible(this Booking _) => true;
}
