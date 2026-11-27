using MIniProject.Models;

namespace MIniProject.ViewModels;

public class CheckInPreviewViewModel
{
    public string PassCode { get; set; } = string.Empty;
    public bool IsMember { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public DateTime? PlanValidUntil { get; set; }
    public string? Identification { get; set; }

    public bool AlreadyInside { get; set; }
    public bool CanCheckIn { get; set; }
    public string? BlockReason { get; set; }
    public string? PromptMessage { get; set; }

    public int? SelectedBookingId { get; set; }
    public List<CheckInBookingRow> TodayBookings { get; set; } = [];
}

public class CheckInBookingRow
{
    public int BookingId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public BookingStatus Status { get; set; }
    public bool IsEligibleWindow { get; set; }
    public DateTime CheckInOpensUtc { get; set; }
}
