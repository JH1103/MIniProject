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

    public List<SelectListItem> EquipmentOptions { get; set; } = [];
    public List<SelectListItem> BookingOptions { get; set; } = [];
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
