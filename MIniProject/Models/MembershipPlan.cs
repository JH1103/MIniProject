using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MIniProject.Models;

public class MembershipPlan
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal AnnualFee { get; set; }

    public int AdvanceBookingDays { get; set; } = 14;

    public int MaxGuestsPerBooking { get; set; } = 4;

    [Column(TypeName = "decimal(5,2)")]
    public decimal DiscountPercent { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}

public class Subscription
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int MembershipPlanId { get; set; }
    public MembershipPlan? MembershipPlan { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    /// <summary>When true, the subscription is marked to renew automatically at expiry.</summary>
    public bool AutoRenew { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CancelledAt { get; set; }

    public bool IsValidOn(DateTime date) =>
        Status == MembershipStatus.Active && date.Date >= StartDate.Date && date.Date <= EndDate.Date;
}
