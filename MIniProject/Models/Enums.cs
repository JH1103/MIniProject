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

/// <summary>Front-desk clearance status for members/guests currently on site.</summary>
public enum ClearanceStatus
{
    CheckedIn = 0,
    UsingFacility = 1,
    CheckedOut = 2,
    Denied = 3
}

public enum EquipmentRentalStatus
{
    /// <summary>Reserved / awaiting staff confirmation that the member took the equipment.</summary>
    Active = 0,
    Returned = 1,
    /// <summary>Linked booking cancelled (shown as Inactive).</summary>
    Cancelled = 2,
    Invalid = 3,
    /// <summary>Staff confirmed the member picked up / took the equipment.</summary>
    Rented = 4,
    /// <summary>Not picked up within 15 minutes after rental start.</summary>
    Inactive = 5,
    /// <summary>Due/end time passed while still rented (not returned).</summary>
    NotReturned = 6
}

public static class AppRoles
{
    public const string Member = "Member";
    public const string Staff = "Staff";
    public const string Administrator = "Administrator";

    public static readonly string[] All = [Member, Staff, Administrator];
}
