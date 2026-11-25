using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;

namespace MIniProject.Services;

public class BookingService
{
    private readonly ApplicationDbContext _db;
    private readonly BookingNotificationService _notifications;

    public BookingService(ApplicationDbContext db, BookingNotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
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
            await CancelLinkedEquipmentRentalsAsync(expired.Select(b => b.Id), ct);
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Marks active equipment rentals linked to the given bookings as Cancelled (inactive)
    /// and restores inventory stock so members cannot keep using them.
    /// </summary>
    public async Task CancelLinkedEquipmentRentalsAsync(IEnumerable<int> bookingIds, CancellationToken ct = default)
    {
        var ids = bookingIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var rentals = await _db.EquipmentRentals
            .Include(r => r.InventoryItem)
            .Where(r =>
                r.BookingId != null &&
                ids.Contains(r.BookingId.Value) &&
                r.Status == EquipmentRentalStatus.Active)
            .ToListAsync(ct);

        foreach (var rental in rentals)
        {
            rental.Status = EquipmentRentalStatus.Cancelled;
            if (rental.InventoryItem is not null)
            {
                rental.InventoryItem.Quantity += rental.Quantity;
            }
        }
    }

    public async Task<Subscription?> GetActiveSubscriptionAsync(string userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.Date;
        // One active plan per account: ignore cancelled/replaced rows.
        return await _db.Subscriptions
            .Include(s => s.MembershipPlan)
            .Where(s => s.UserId == userId &&
                        s.Status == MembershipStatus.Active &&
                        s.CancelledAt == null &&
                        s.StartDate <= now &&
                        s.EndDate >= now)
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.EndDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<(bool Ok, string? Error, Booking? Booking)> CreateBookingAsync(
        string memberId,
        int facilityId,
        DateTime startLocal,
        DateTime endLocal,
        IEnumerable<(string FullName, string? IdNumber, string? Phone)> guests,
        string? notes = null,
        bool isRecurring = false,
        IEnumerable<int>? selectedEquipmentIds = null,
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
        var facilityAmount = Math.Max(0, baseAmount - discount);

        // Resolve equipment up front so invoice/booking totals include full-price rental fees
        // before any zero-amount auto-confirm path runs.
        var equipmentIdList = (selectedEquipmentIds ?? Enumerable.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        List<InventoryItem> equipmentItems = equipmentIdList.Count == 0
            ? []
            : await _db.InventoryItems
                .Where(i =>
                    equipmentIdList.Contains(i.Id) &&
                    i.FacilityId == facilityId &&
                    i.Condition != InventoryCondition.Retired &&
                    i.Quantity >= 1)
                .ToListAsync(ct);

        decimal equipmentTotal = 0m;
        foreach (var _ in equipmentItems)
        {
            equipmentTotal += EquipmentPricing.CalculateFee(1, startUtc, endUtc);
        }

        var amount = Math.Round(facilityAmount + equipmentTotal, 2);
        var equipmentNames = equipmentItems.Select(i => i.Name).ToList();
        var bookingNotes = notes;
        if (equipmentNames.Count > 0)
        {
            var equipmentNote = "Equipment: " + string.Join(", ", equipmentNames);
            bookingNotes = string.IsNullOrWhiteSpace(bookingNotes)
                ? equipmentNote
                : $"{bookingNotes}\n{equipmentNote}";
        }

        var booking = new Booking
        {
            MemberId = memberId,
            FacilityId = facilityId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Amount = amount,
            Notes = bookingNotes,
            IsRecurring = isRecurring,
            RecurrenceGroupId = isRecurring ? Guid.NewGuid().ToString("N") : null,
            CreatedAt = DateTime.UtcNow,
            PaymentHoldExpiresAt = DateTime.UtcNow.AddMinutes(facility.PaymentHoldMinutes),
            Status = facility.BookingMode == BookingMode.ApprovalRequired
                ? BookingStatus.Pending
                : BookingStatus.AwaitingPayment
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync(ct);

        foreach (var item in equipmentItems)
        {
            item.Quantity -= 1;
            _db.EquipmentRentals.Add(new EquipmentRental
            {
                MemberId = memberId,
                InventoryItemId = item.Id,
                BookingId = booking.Id,
                Quantity = 1,
                StartUtc = startUtc,
                DueUtc = endUtc,
                Status = EquipmentRentalStatus.Active,
                Fee = EquipmentPricing.CalculateFee(1, startUtc, endUtc),
                Notes = $"Rented with booking #{booking.Id}"
            });
        }

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
                Description = equipmentTotal > 0
                    ? $"Booking for {facility.Name} + equipment rental"
                    : $"Booking for {facility.Name}",
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

        if (booking.Status == BookingStatus.Confirmed)
        {
            await _notifications.SendConfirmationAsync(booking.Id, ct);
        }

        return (true, null, booking);
    }

    public async Task<(bool Ok, string? Error)> UpdateBookingAsync(
        int bookingId,
        string memberId,
        DateTime startLocal,
        DateTime endLocal,
        IEnumerable<(string FullName, string? IdNumber, string? Phone)> guests,
        string? notes = null,
        CancellationToken ct = default)
    {
        await ReleaseExpiredHoldsAsync(ct);

        var booking = await _db.Bookings
            .Include(b => b.Facility)
            .Include(b => b.Guests)
            .Include(b => b.Invoice)
            .Include(b => b.EquipmentRentals)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null)
        {
            return (false, "Booking not found.");
        }

        if (booking.MemberId != memberId)
        {
            return (false, "You can only update your own bookings.");
        }

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.NoShow)
        {
            return (false, "This booking can no longer be updated.");
        }

        var facility = booking.Facility;
        if (facility is null || !facility.IsActive || facility.Status != FacilityStatus.Available)
        {
            return (false, "Facility is currently unavailable.");
        }

        var subscription = await GetActiveSubscriptionAsync(memberId, ct);
        if (subscription?.MembershipPlan is null)
        {
            return (false, "An active membership is required to update bookings.");
        }

        var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();
        var endUtc = DateTime.SpecifyKind(endLocal, DateTimeKind.Local).ToUniversalTime();
        if (endUtc <= startUtc)
        {
            return (false, "End time must be after start time.");
        }

        if (startUtc < DateTime.UtcNow)
        {
            return (false, "Cannot move a booking into the past.");
        }

        var localDate = startLocal.Date;
        var open = localDate.Add(facility.OpenTime);
        var close = localDate.Add(facility.CloseTime);
        if (startLocal < open || endLocal > close)
        {
            return (false, "Selected time is outside facility operating hours.");
        }

        var guestList = guests.Where(g => !string.IsNullOrWhiteSpace(g.FullName)).ToList();
        if (guestList.Count > subscription.MembershipPlan.MaxGuestsPerBooking)
        {
            return (false, $"Your plan allows up to {subscription.MembershipPlan.MaxGuestsPerBooking} guests per booking.");
        }

        var blocked = await _db.FacilityBlockOuts.AnyAsync(b =>
            b.FacilityId == facility.Id &&
            b.StartUtc < endUtc &&
            b.EndUtc > startUtc, ct);
        if (blocked)
        {
            return (false, "Facility is blocked for maintenance or a private event.");
        }

        var conflict = await _db.Bookings.AnyAsync(b =>
            b.Id != booking.Id &&
            b.FacilityId == facility.Id &&
            b.Status != BookingStatus.Cancelled &&
            b.StartUtc < endUtc &&
            b.EndUtc > startUtc, ct);
        if (conflict)
        {
            return (false, "This time slot is no longer available.");
        }

        var hours = (decimal)(endUtc - startUtc).TotalHours;
        var baseAmount = Math.Round(facility.HourlyRate * hours, 2);
        var discount = Math.Round(baseAmount * (subscription.MembershipPlan.DiscountPercent / 100m), 2);
        var facilityAmount = Math.Max(0, baseAmount - discount);

        booking.StartUtc = startUtc;
        booking.EndUtc = endUtc;
        booking.Notes = notes;

        // Reprice active equipment for the new window at full rate (no member discount).
        decimal equipmentTotal = 0m;
        foreach (var rental in booking.EquipmentRentals.Where(r => r.Status == EquipmentRentalStatus.Active))
        {
            rental.StartUtc = startUtc;
            rental.DueUtc = endUtc;
            rental.Fee = EquipmentPricing.CalculateFee(rental.Quantity, startUtc, endUtc);
            equipmentTotal += rental.Fee;
        }

        var amount = Math.Round(facilityAmount + equipmentTotal, 2);
        booking.Amount = amount;

        _db.BookingGuests.RemoveRange(booking.Guests);
        foreach (var guestInput in guestList)
        {
            var guest = await _db.Guests.FirstOrDefaultAsync(g =>
                g.HostMemberId == memberId &&
                g.FullName == guestInput.FullName.Trim(), ct);
            if (guest is null)
            {
                guest = new Guest
                {
                    HostMemberId = memberId,
                    FullName = guestInput.FullName.Trim(),
                    IdentificationNumber = guestInput.IdNumber,
                    Phone = guestInput.Phone
                };
                _db.Guests.Add(guest);
            }

            booking.Guests.Add(new BookingGuest { Guest = guest });
        }

        if (booking.Invoice is not null && booking.Invoice.Status == InvoiceStatus.Unpaid)
        {
            booking.Invoice.Amount = amount;
            booking.Invoice.Description = equipmentTotal > 0
                ? $"Booking for {facility.Name} + equipment rental"
                : $"Booking for {facility.Name}";
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = memberId,
            Action = "BookingUpdated",
            Entity = $"Booking:{booking.Id}",
            Details = $"{facility.Name} {startUtc:u} - {endUtc:u}"
        });

        await _db.SaveChangesAsync(ct);
        return (true, null);
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

        if (booking.Status == BookingStatus.Confirmed)
        {
            await _notifications.SendConfirmationAsync(booking.Id, ct);
        }

        return (true, null);
    }

    public List<(DateTime Start, DateTime End, bool Available, string State)> GetDaySlots(Facility facility, DateTime day, IEnumerable<Booking> bookings, IEnumerable<FacilityBlockOut> blockOuts)
    {
        var slots = new List<(DateTime Start, DateTime End, bool Available, string State)>();
        var cursor = day.Date.Add(facility.OpenTime);
        var close = day.Date.Add(facility.CloseTime);
        var duration = TimeSpan.FromMinutes(facility.SlotDurationMinutes);

        while (cursor + duration <= close)
        {
            var end = cursor + duration;
            var startUtc = DateTime.SpecifyKind(cursor, DateTimeKind.Local).ToUniversalTime();
            var endUtc = DateTime.SpecifyKind(end, DateTimeKind.Local).ToUniversalTime();

            var overlapping = bookings
                .Where(b => b.Status != BookingStatus.Cancelled && b.StartUtc < endUtc && b.EndUtc > startUtc)
                .ToList();

            var blocked = blockOuts.Any(b => b.StartUtc < endUtc && b.EndUtc > startUtc);
            var past = startUtc < DateTime.UtcNow;
            string state;
            if (blocked || facility.Status != FacilityStatus.Available || past)
            {
                state = "blocked";
            }
            else if (overlapping.Any(b => b.Status == BookingStatus.Confirmed))
            {
                state = "booked";
            }
            else if (overlapping.Any())
            {
                state = "pending";
            }
            else
            {
                state = "available";
            }

            slots.Add((cursor, end, state == "available", state));
            cursor = end;
        }

        return slots;
    }
}

internal static class BookingInvoiceExtensions
{
    public static bool InvoiceIdCompatible(this Booking _) => true;
}
