using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace MIniProject.Models;

public class ApplicationUser : IdentityUser
{
    [Required, StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Address { get; set; }

    [StringLength(50)]
    public string? IdentificationNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool PdpaConsent { get; set; }

    public DateTime? PdpaConsentAt { get; set; }

    public MembershipStatus MembershipStatus { get; set; } = MembershipStatus.Expired;

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<Guest> HostedGuests { get; set; } = new List<Guest>();
}
