using System.ComponentModel.DataAnnotations;

namespace MIniProject.Models;

public class Guest
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(50)]
    public string? IdentificationNumber { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [EmailAddress, StringLength(120)]
    public string? Email { get; set; }

    [Required]
    public string HostMemberId { get; set; } = string.Empty;
    public ApplicationUser? HostMember { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VisitorPass> Passes { get; set; } = new List<VisitorPass>();
    public ICollection<BookingGuest> BookingGuests { get; set; } = new List<BookingGuest>();
}

public class VisitorPass
{
    public int Id { get; set; }

    public int GuestId { get; set; }
    public Guest? Guest { get; set; }

    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }

    [Required, StringLength(64)]
    public string PassCode { get; set; } = string.Empty;

    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }

    public bool IsRevoked { get; set; }

    public ICollection<CheckInRecord> CheckIns { get; set; } = new List<CheckInRecord>();
}

public class CheckInRecord
{
    public int Id { get; set; }

    public int VisitorPassId { get; set; }
    public VisitorPass? VisitorPass { get; set; }

    public DateTime CheckedInAt { get; set; }
    public DateTime? CheckedOutAt { get; set; }

    public string? CheckedInByUserId { get; set; }
    public string? CheckedOutByUserId { get; set; }
}

/// <summary>Front-desk check-in for members using their digital membership QR.</summary>
public class MemberCheckIn
{
    public int Id { get; set; }

    [Required]
    public string MemberId { get; set; } = string.Empty;
    public ApplicationUser? Member { get; set; }

    public DateTime CheckedInAt { get; set; }
    public DateTime? CheckedOutAt { get; set; }

    public string? CheckedInByUserId { get; set; }
    public string? CheckedOutByUserId { get; set; }
}
