using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MIniProject.ViewModels;

public enum StaffActivityActionType
{
    [Display(Name = "All actions")]
    All = 0,
    [Display(Name = "Member check-in")]
    MemberCheckIn = 1,
    [Display(Name = "Guest check-in")]
    GuestCheckIn = 2,
    [Display(Name = "Equipment taken")]
    EquipmentPickup = 3
}

public class StaffActivityFilterViewModel
{
    [DataType(DataType.Date)]
    [Display(Name = "From")]
    public DateTime? FromDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "To")]
    public DateTime? ToDate { get; set; }

    [Display(Name = "Staff / admin")]
    public string? StaffUserId { get; set; }

    [Display(Name = "Member")]
    public string? MemberQuery { get; set; }

    [Display(Name = "Action type")]
    public StaffActivityActionType ActionType { get; set; } = StaffActivityActionType.All;

    public List<SelectListItem> StaffOptions { get; set; } = [];
}

public class StaffActivityRowViewModel
{
    public DateTime OccurredAtUtc { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string ActionTypeKey { get; set; } = string.Empty;
    public string StaffUserId { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    public string? MemberId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public class StaffActivityReportViewModel
{
    public StaffActivityFilterViewModel Filter { get; set; } = new();
    public List<StaffActivityRowViewModel> Rows { get; set; } = [];
    public int TotalCount => Rows.Count;
    public int MemberCheckInCount { get; set; }
    public int GuestCheckInCount { get; set; }
    public int EquipmentPickupCount { get; set; }
}
