using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MIniProject.Models;

public class Booking
{
    public int Id { get; set; }

    [Required]
    public string MemberId { get; set; } = string.Empty;
    public ApplicationUser? Member { get; set; }

    public int FacilityId { get; set; }
    public Facility? Facility { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? PaymentHoldExpiresAt { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public bool IsRecurring { get; set; }
    public string? RecurrenceGroupId { get; set; }

    public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public Invoice? Invoice { get; set; }
    public ICollection<BookingGuest> Guests { get; set; } = new List<BookingGuest>();
}

public class BookingGuest
{
    public int Id { get; set; }

    public int BookingId { get; set; }
    public Booking? Booking { get; set; }

    public int GuestId { get; set; }
    public Guest? Guest { get; set; }
}

public class WaitingListEntry
{
    public int Id { get; set; }

    [Required]
    public string MemberId { get; set; } = string.Empty;
    public ApplicationUser? Member { get; set; }

    public int FacilityId { get; set; }
    public Facility? Facility { get; set; }

    public DateTime DesiredStartUtc { get; set; }
    public DateTime DesiredEndUtc { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsNotified { get; set; }
}
