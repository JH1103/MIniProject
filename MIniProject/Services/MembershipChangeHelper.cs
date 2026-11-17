using MIniProject.Models;

namespace MIniProject.Services;

public static class MembershipChangeHelper
{
    public const decimal DowngradeRefundRate = 0.75m;

    public static string DisplayStatus(Subscription subscription)
    {
        return subscription.Status switch
        {
            MembershipStatus.Active when subscription.CancelledAt is not null => "Inactive",
            MembershipStatus.Active => "Active",
            MembershipStatus.Suspended => "Pending payment",
            MembershipStatus.Expired => "Inactive",
            _ => subscription.Status.ToString()
        };
    }

    public static void DeactivateSubscription(Subscription subscription, DateTime? endDate = null)
    {
        subscription.Status = MembershipStatus.Expired;
        subscription.AutoRenew = false;
        subscription.CancelledAt ??= DateTime.UtcNow;
        if (endDate.HasValue)
        {
            subscription.EndDate = endDate.Value.Date;
        }
    }
}
