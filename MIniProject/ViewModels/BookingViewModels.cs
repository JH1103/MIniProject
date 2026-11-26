using System.ComponentModel.DataAnnotations;
using MIniProject.Models;

namespace MIniProject.ViewModels;

public class BookingCreateViewModel
{
    public int FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public string? FacilityImageUrl { get; set; }
    public BookingMode BookingMode { get; set; }
    public decimal HourlyRate { get; set; }
    public int MaxGuests { get; set; } = 2;
    public string MemberDisplayName { get; set; } = string.Empty;
    public string MembershipPlanName { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }

    [Required, DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    [Required]
    public string SlotStart { get; set; } = string.Empty;

    [Required]
    public string SlotEnd { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Notes { get; set; }

    public bool IsRecurring { get; set; }

    public List<GuestInputModel> Guests { get; set; } = [new(), new()];

    public List<SlotOption> AvailableSlots { get; set; } = [];

    public List<EquipmentOption> Equipment { get; set; } = [];

    /// <summary>Inventory item IDs selected during booking (creates linked equipment rentals).</summary>
    public List<int> SelectedEquipmentIds { get; set; } = [];
}

public class GuestInputModel
{
    public string? FullName { get; set; }
    public string? IdentificationNumber { get; set; }
    public string? Phone { get; set; }
}

public class SlotOption
{
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public bool Available { get; set; }
    public string State { get; set; } = "available";
    public string Label => $"{Start} - {End}";
}

public class EquipmentOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Condition { get; set; } = string.Empty;

    /// <summary>Full daily rental rate (member discount does not apply).</summary>
    public decimal DailyFee { get; set; } = 15m;
}

public class MemberDashboardViewModel
{
    public ApplicationUser User { get; set; } = null!;
    public Subscription? Subscription { get; set; }
    public List<Booking> UpcomingBookings { get; set; } = [];
    public List<Booking> BookingHistory { get; set; } = [];
    public List<EquipmentRental> EquipmentRentals { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
    public List<VisitorPass> Passes { get; set; } = [];
    public string MemberPassCode { get; set; } = string.Empty;
    public string MemberQrBase64 { get; set; } = string.Empty;
    public List<MembershipPlan> UpgradePlans { get; set; } = [];
}
