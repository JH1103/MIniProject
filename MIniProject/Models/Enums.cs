namespace MIniProject.Models;

public enum MembershipStatus
{
    Active = 0,
    Expired = 1,
    Suspended = 2
}

public enum FacilityStatus
{
    Available = 0,
    Maintenance = 1,
    Closed = 2
}

public enum BookingMode
{
    InstantConfirmation = 0,
    ApprovalRequired = 1
}

public enum BookingStatus
{
    Pending = 0,
    AwaitingPayment = 1,
    Confirmed = 2,
    Cancelled = 3,
    Completed = 4,
    NoShow = 5
}

public enum InvoiceStatus
{
    Unpaid = 0,
    Paid = 1,
    Overdue = 2,
    Refunded = 3,
    PartiallyRefunded = 4,
    Cancelled = 5
}

public enum PaymentStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Refunded = 3
}

public enum RefundStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Completed = 3
}

public enum InventoryCondition
{
    Good = 0,
    Fair = 1,
    Damaged = 2,
    Retired = 3
}

public static class AppRoles
{
    public const string Member = "Member";
    public const string Staff = "Staff";
    public const string Administrator = "Administrator";

    public static readonly string[] All = [Member, Staff, Administrator];
}
