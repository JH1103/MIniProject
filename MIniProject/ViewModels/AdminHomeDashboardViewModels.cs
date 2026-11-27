namespace MIniProject.ViewModels;

/// <summary>
/// Home-page statistics dashboard for Admin / Staff.
/// Window: last <see cref="WindowDays"/> days (UTC), unless noted as all-time summary tiles.
/// </summary>
public class AdminHomeDashboardViewModel
{
    public int WindowDays { get; set; } = 90;

    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }

    public int BookingsInWindow { get; set; }
    public int RentalsInWindow { get; set; }
    public int CheckInsInWindow { get; set; }
    public int ActiveFacilities { get; set; }

    /// <summary>Daily booking counts (bar) for trend.</summary>
    public List<NamedCountStat> BookingsByDay { get; set; } = [];

    /// <summary>Booking status breakdown (pie).</summary>
    public List<NamedCountStat> BookingsByStatus { get; set; } = [];

    /// <summary>Equipment rental volume by catalog/inventory name (bar).</summary>
    public List<NamedCountStat> RentalsByEquipment { get; set; } = [];

    /// <summary>Facility usage by booking count (pie).</summary>
    public List<NamedCountStat> FacilityUsage { get; set; } = [];

    /// <summary>Top members by combined bookings + check-ins (bar).</summary>
    public List<TopMemberStat> TopMembers { get; set; } = [];
}

public class NamedCountStat
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class TopMemberStat
{
    public string MemberName { get; set; } = string.Empty;
    public int BookingCount { get; set; }
    public int CheckInCount { get; set; }
    public int TotalActivity => BookingCount + CheckInCount;
}
