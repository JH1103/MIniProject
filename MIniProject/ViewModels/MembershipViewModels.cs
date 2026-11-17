using System.ComponentModel.DataAnnotations;
using MIniProject.Models;

namespace MIniProject.ViewModels;

public class SubscribeConfirmViewModel
{
    public int PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal AnnualFee { get; set; }
    public int AdvanceBookingDays { get; set; }
    public int MaxGuestsPerBooking { get; set; }
    public decimal DiscountPercent { get; set; }

    [Display(Name = "Enable auto-renewal")]
    public bool AutoRenew { get; set; } = true;

    public bool IsPlanChange { get; set; }
    public string? CurrentPlanName { get; set; }
    public decimal CurrentAnnualFee { get; set; }
    public DateTime? KeptEndDate { get; set; }
    public string ChangeType { get; set; } = "New"; // New | Upgrade | Downgrade
    public decimal PriceDifference { get; set; }
    public decimal AmountDue { get; set; }
    public decimal RefundCredit { get; set; }
}

public class MembershipManageViewModel
{
    public Subscription? Subscription { get; set; }
    public List<Subscription> History { get; set; } = [];
}
