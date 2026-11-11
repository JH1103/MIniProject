using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;

namespace MIniProject.Services;

public class ReportService
{
    private readonly ApplicationDbContext _db;

    public ReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var bookingsToday = await _db.Bookings.CountAsync(b =>
            b.StartUtc >= today && b.StartUtc < tomorrow && b.Status != BookingStatus.Cancelled, ct);

        var pendingApprovals = await _db.Bookings.CountAsync(b => b.Status == BookingStatus.Pending, ct);
        var activeMembers = await _db.Users.CountAsync(u => u.MembershipStatus == MembershipStatus.Active, ct);
        var revenueMonth = await _db.PaymentTransactions
            .Where(p => p.Status == PaymentStatus.Completed && p.CompletedAt != null && p.CompletedAt.Value.Month == DateTime.UtcNow.Month)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var facilityUsage = await _db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed)
            .GroupBy(b => b.Facility!.Name)
            .Select(g => new FacilityUsageStat { FacilityName = g.Key, BookingCount = g.Count() })
            .OrderByDescending(x => x.BookingCount)
            .Take(8)
            .ToListAsync(ct);

        var peakHours = await _db.Bookings
            .Where(b => b.Status != BookingStatus.Cancelled)
            .GroupBy(b => b.StartUtc.Hour)
            .Select(g => new PeakHourStat { Hour = g.Key, Count = g.Count() })
            .OrderBy(x => x.Hour)
            .ToListAsync(ct);

        return new DashboardStats
        {
            BookingsToday = bookingsToday,
            PendingApprovals = pendingApprovals,
            ActiveMembers = activeMembers,
            RevenueThisMonth = revenueMonth,
            FacilityUsage = facilityUsage,
            PeakHours = peakHours
        };
    }
}

public class DashboardStats
{
    public int BookingsToday { get; set; }
    public int PendingApprovals { get; set; }
    public int ActiveMembers { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public List<FacilityUsageStat> FacilityUsage { get; set; } = [];
    public List<PeakHourStat> PeakHours { get; set; } = [];
}

public class FacilityUsageStat
{
    public string FacilityName { get; set; } = string.Empty;
    public int BookingCount { get; set; }
}

public class PeakHourStat
{
    public int Hour { get; set; }
    public int Count { get; set; }
}
