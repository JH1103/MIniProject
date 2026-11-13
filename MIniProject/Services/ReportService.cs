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
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonth = monthStart.AddMonths(1);

        var bookingsToday = await _db.Bookings.CountAsync(b =>
            b.StartUtc >= today && b.StartUtc < tomorrow && b.Status != BookingStatus.Cancelled, ct);

        var pendingApprovals = await _db.Bookings.CountAsync(b => b.Status == BookingStatus.Pending, ct);
        var activeMembers = await _db.Users.CountAsync(u => u.MembershipStatus == MembershipStatus.Active, ct);
        var totalFacilities = await _db.Facilities.CountAsync(f => f.IsActive, ct);
        var registeredMembers = await _db.Users.CountAsync(ct);

        var revenueMonth = await _db.PaymentTransactions
            .Where(p => p.Status == PaymentStatus.Completed &&
                        p.CompletedAt != null &&
                        p.CompletedAt.Value >= monthStart &&
                        p.CompletedAt.Value < nextMonth)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var totalBookings = await _db.Bookings.CountAsync(b => b.Status != BookingStatus.Cancelled, ct);
        var confirmedBookings = await _db.Bookings.CountAsync(b =>
            b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed, ct);
        var cancelledBookings = await _db.Bookings.CountAsync(b => b.Status == BookingStatus.Cancelled, ct);
        var cancellationRatio = totalBookings + cancelledBookings == 0
            ? 0m
            : Math.Round(100m * cancelledBookings / (totalBookings + cancelledBookings), 1);

        var facilityUsage = await _db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed)
            .GroupBy(b => b.Facility!.Name)
            .Select(g => new FacilityUsageStat
            {
                FacilityName = g.Key,
                BookingCount = g.Count(),
                Revenue = g.Sum(x => x.Amount)
            })
            .OrderByDescending(x => x.Revenue)
            .ThenByDescending(x => x.BookingCount)
            .Take(10)
            .ToListAsync(ct);

        var peakHours = await _db.Bookings
            .Where(b => b.Status != BookingStatus.Cancelled)
            .GroupBy(b => b.StartUtc.Hour)
            .Select(g => new PeakHourStat { Hour = g.Key, Count = g.Count() })
            .OrderBy(x => x.Hour)
            .ToListAsync(ct);

        var membershipTiers = await _db.MembershipPlans
            .Where(p => p.IsActive)
            .Select(p => new MembershipTierStat
            {
                PlanName = p.Name,
                MemberCount = p.Subscriptions.Count(s => s.Status == MembershipStatus.Active && s.EndDate >= today),
                AdvanceBookingDays = p.AdvanceBookingDays,
                DiscountPercent = p.DiscountPercent
            })
            .OrderByDescending(p => p.DiscountPercent)
            .ToListAsync(ct);

        return new DashboardStats
        {
            BookingsToday = bookingsToday,
            PendingApprovals = pendingApprovals,
            ActiveMembers = activeMembers,
            RegisteredMembers = registeredMembers,
            TotalFacilities = totalFacilities,
            TotalBookings = confirmedBookings,
            CancellationRatio = cancellationRatio,
            RevenueThisMonth = revenueMonth,
            FacilityUsage = facilityUsage,
            PeakHours = peakHours,
            MembershipTiers = membershipTiers
        };
    }
}

public class DashboardStats
{
    public int BookingsToday { get; set; }
    public int PendingApprovals { get; set; }
    public int ActiveMembers { get; set; }
    public int RegisteredMembers { get; set; }
    public int TotalFacilities { get; set; }
    public int TotalBookings { get; set; }
    public decimal CancellationRatio { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public List<FacilityUsageStat> FacilityUsage { get; set; } = [];
    public List<PeakHourStat> PeakHours { get; set; } = [];
    public List<MembershipTierStat> MembershipTiers { get; set; } = [];
}

public class FacilityUsageStat
{
    public string FacilityName { get; set; } = string.Empty;
    public int BookingCount { get; set; }
    public decimal Revenue { get; set; }
}

public class PeakHourStat
{
    public int Hour { get; set; }
    public int Count { get; set; }
}

public class MembershipTierStat
{
    public string PlanName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public int AdvanceBookingDays { get; set; }
    public decimal DiscountPercent { get; set; }
}
