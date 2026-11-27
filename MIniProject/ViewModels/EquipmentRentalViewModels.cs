using System.ComponentModel.DataAnnotations;
using MIniProject.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MIniProject.ViewModels;

public class EquipmentRentalFormViewModel
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Equipment")]
    public int InventoryItemId { get; set; }

    [Range(1, 50)]
    public int Quantity { get; set; } = 1;

    [Required, DataType(DataType.Date)]
    [Display(Name = "Start date")]
    public DateTime StartDate { get; set; } = DateTime.Today;

    [Required]
    [Display(Name = "Start time")]
    public string StartTime { get; set; } = "09:00";

    [Required, DataType(DataType.Date)]
    [Display(Name = "Due date")]
    public DateTime DueDate { get; set; } = DateTime.Today;

    [Required]
    [Display(Name = "Due time")]
    public string DueTime { get; set; } = "18:00";

    public int? BookingId { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public EquipmentRentalStatus Status { get; set; } = EquipmentRentalStatus.Active;

    /// <summary>When true, start/due fields mirror the linked booking and cannot be edited.</summary>
    public bool ScheduleLocked { get; set; }

    /// <summary>When true (member create), a linked booking is required — no orphan rentals.</summary>
    public bool RequiresLinkedBooking { get; set; }

    /// <summary>Count of future/current (not yet ended) linkable bookings for this member.</summary>
    public int LinkableBookingCount { get; set; }

    /// <summary>Display label when exactly one booking is auto-selected (chooser hidden).</summary>
    public string? SoleLinkedBookingLabel { get; set; }

    /// <summary>Guidance on what equipment can be rented for the linked booking's facility.</summary>
    public string EquipmentGuidance { get; set; } =
        "Select a linked booking to lock the rental window and show equipment for that facility.";

    public List<SelectListItem> EquipmentOptions { get; set; } = [];
    public List<SelectListItem> BookingOptions { get; set; } = [];

    /// <summary>Client-side catalog for filtering equipment by facility when a booking is linked.</summary>
    public List<EquipmentCatalogItem> EquipmentCatalog { get; set; } = [];

    /// <summary>Client-side booking schedules used to lock the rental window.</summary>
    public List<LinkedBookingSchedule> LinkedBookingSchedules { get; set; } = [];
}

public class EquipmentCatalogItem
{
    public int Id { get; set; }
    public int FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Label => $"{FacilityName} · {Name} (qty {Quantity})";
}

public class LinkedBookingSchedule
{
    public int Id { get; set; }
    public int FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;
    public string DueTime { get; set; } = string.Empty;
    public List<string> EquipmentNames { get; set; } = [];
}

public class AdminMemberFormViewModel
{
    public string? Id { get; set; }

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Phone, StringLength(30)]
    public string? PhoneNumber { get; set; }

    [StringLength(50)]
    [Display(Name = "Identification number")]
    public string? IdentificationNumber { get; set; }

    [StringLength(200)]
    public string? Address { get; set; }

    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8)]
    public string? Password { get; set; }

    public MembershipStatus MembershipStatus { get; set; } = MembershipStatus.Expired;
}
