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

    public List<string> SelectedEquipment { get; set; } = [];
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
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Condition { get; set; } = string.Empty;
}

public class MemberDashboardViewModel
{
    public ApplicationUser User { get; set; } = null!;
    public Subscription? Subscription { get; set; }
    public List<Booking> UpcomingBookings { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
    public List<VisitorPass> Passes { get; set; } = [];
    public string MemberPassCode { get; set; } = string.Empty;
    public string MemberQrBase64 { get; set; } = string.Empty;
    public List<MembershipPlan> UpgradePlans { get; set; } = [];
}
