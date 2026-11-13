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
}

public class MembershipManageViewModel
{
    public Subscription? Subscription { get; set; }
    public List<Subscription> History { get; set; } = [];
}
