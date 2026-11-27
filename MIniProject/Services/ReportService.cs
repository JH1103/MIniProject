using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.ViewModels;

namespace MIniProject.Services;

public class ReportService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReportService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
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

    public async Task<StaffActivityReportViewModel> GetStaffActivityReportAsync(
        StaffActivityFilterViewModel filter,
        CancellationToken ct = default)
    {
        filter.FromDate ??= DateTime.UtcNow.Date.AddDays(-30);
        filter.ToDate ??= DateTime.UtcNow.Date;

        var fromUtc = DateTime.SpecifyKind(filter.FromDate.Value.Date, DateTimeKind.Utc);
        var toUtcExclusive = DateTime.SpecifyKind(filter.ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
        var memberQuery = filter.MemberQuery?.Trim();
        var staffId = string.IsNullOrWhiteSpace(filter.StaffUserId) ? null : filter.StaffUserId.Trim();
        var action = filter.ActionType;

        var rows = new List<StaffActivityRowViewModel>();

        if (action is StaffActivityActionType.All or StaffActivityActionType.MemberCheckIn)
        {
            var memberCheckIns = await _db.MemberCheckIns
                .AsNoTracking()
                .Include(c => c.Member)
                .Include(c => c.Facility)
                .Include(c => c.Booking)!.ThenInclude(b => b!.Facility)
                .Where(c => c.CheckedInAt >= fromUtc && c.CheckedInAt < toUtcExclusive)
                .Where(c => staffId == null || c.CheckedInByUserId == staffId)
                .OrderByDescending(c => c.CheckedInAt)
                .Take(1000)
                .ToListAsync(ct);

            foreach (var c in memberCheckIns)
            {
                var memberName = c.Member?.FullName ?? "(unknown member)";
                if (!MatchesMemberQuery(memberQuery, memberName, c.MemberId))
                {
                    continue;
                }

                var facility = c.Facility?.Name ?? c.Booking?.Facility?.Name ?? "facility";
                rows.Add(new StaffActivityRowViewModel
                {
                    OccurredAtUtc = c.CheckedInAt,
                    ActionType = "Member check-in",
                    ActionTypeKey = nameof(StaffActivityActionType.MemberCheckIn),
                    StaffUserId = c.CheckedInByUserId ?? "",
                    StaffName = ResolveActorName(c.CheckedInByName, c.CheckedInByUserId),
                    MemberName = memberName,
                    MemberId = c.MemberId,
                    Reference = c.BookingId is int bid ? $"Booking #{bid}" : "—",
                    Detail = facility
                });
            }
        }

        if (action is StaffActivityActionType.All or StaffActivityActionType.GuestCheckIn)
        {
            var guestCheckIns = await _db.CheckInRecords
                .AsNoTracking()
                .Include(c => c.VisitorPass)!.ThenInclude(p => p!.Guest)
                .Include(c => c.VisitorPass)!.ThenInclude(p => p!.Booking)!.ThenInclude(b => b!.Facility)
                .Where(c => c.CheckedInAt >= fromUtc && c.CheckedInAt < toUtcExclusive)
                .Where(c => staffId == null || c.CheckedInByUserId == staffId)
                .OrderByDescending(c => c.CheckedInAt)
                .Take(1000)
                .ToListAsync(ct);

            foreach (var c in guestCheckIns)
            {
                var guestName = c.VisitorPass?.Guest?.FullName ?? "(unknown guest)";
                if (!MatchesMemberQuery(memberQuery, guestName, null))
                {
                    continue;
                }

                var bookingId = c.VisitorPass?.BookingId;
                var facility = c.VisitorPass?.Booking?.Facility?.Name ?? "facility";
                rows.Add(new StaffActivityRowViewModel
                {
                    OccurredAtUtc = c.CheckedInAt,
                    ActionType = "Guest check-in",
                    ActionTypeKey = nameof(StaffActivityActionType.GuestCheckIn),
                    StaffUserId = c.CheckedInByUserId ?? "",
                    StaffName = ResolveActorName(c.CheckedInByName, c.CheckedInByUserId),
                    MemberName = guestName,
                    MemberId = c.VisitorPass?.Guest?.HostMemberId,
                    Reference = bookingId is int bid ? $"Booking #{bid}" : "—",
                    Detail = $"Guest · {facility}"
                });
            }
        }

        if (action is StaffActivityActionType.All or StaffActivityActionType.EquipmentPickup)
        {
            var pickups = await _db.EquipmentRentals
                .AsNoTracking()
                .Include(r => r.Member)
                .Include(r => r.InventoryItem)
                .Include(r => r.Booking)
                .Where(r => r.PickedUpAt != null &&
                            r.PickedUpAt >= fromUtc &&
                            r.PickedUpAt < toUtcExclusive)
                .Where(r => staffId == null || r.PickedUpByUserId == staffId)
                .OrderByDescending(r => r.PickedUpAt)
                .Take(1000)
                .ToListAsync(ct);

            foreach (var r in pickups)
            {
                var memberName = r.Member?.FullName ?? "(unknown member)";
                if (!MatchesMemberQuery(memberQuery, memberName, r.MemberId))
                {
                    continue;
                }

                var equipment = r.InventoryItem?.Name ?? "equipment";
                rows.Add(new StaffActivityRowViewModel
                {
                    OccurredAtUtc = r.PickedUpAt!.Value,
                    ActionType = "Equipment taken",
                    ActionTypeKey = nameof(StaffActivityActionType.EquipmentPickup),
                    StaffUserId = r.PickedUpByUserId ?? "",
                    StaffName = ResolveActorName(r.PickedUpByName, r.PickedUpByUserId),
                    MemberName = memberName,
                    MemberId = r.MemberId,
                    Reference = r.BookingId is int bid
                        ? $"Rental #{r.Id} · Booking #{bid}"
                        : $"Rental #{r.Id}",
                    Detail = $"{equipment} × {r.Quantity}"
                });
            }
        }

        // Resolve any remaining blank staff names from current user directory.
        var missingIds = rows
            .Where(r => string.IsNullOrWhiteSpace(r.StaffName) && !string.IsNullOrWhiteSpace(r.StaffUserId))
            .Select(r => r.StaffUserId)
            .Distinct()
            .ToList();
        if (missingIds.Count > 0)
        {
            var names = await _db.Users
                .AsNoTracking()
                .Where(u => missingIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName })
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

            foreach (var row in rows.Where(r => string.IsNullOrWhiteSpace(r.StaffName)))
            {
                if (!string.IsNullOrWhiteSpace(row.StaffUserId) &&
                    names.TryGetValue(row.StaffUserId, out var name))
                {
                    row.StaffName = name;
                }
                else if (string.IsNullOrWhiteSpace(row.StaffUserId))
                {
                    row.StaffName = "(not recorded)";
                }
                else
                {
                    row.StaffName = "(unknown staff)";
                }
            }
        }
        else
        {
            foreach (var row in rows.Where(r => string.IsNullOrWhiteSpace(r.StaffName)))
            {
                row.StaffName = string.IsNullOrWhiteSpace(row.StaffUserId)
                    ? "(not recorded)"
                    : "(unknown staff)";
            }
        }

        filter.StaffOptions = await BuildStaffOptionsAsync(staffId, ct);

        var ordered = rows.OrderByDescending(r => r.OccurredAtUtc).ToList();
        return new StaffActivityReportViewModel
        {
            Filter = filter,
            Rows = ordered,
            MemberCheckInCount = ordered.Count(r => r.ActionTypeKey == nameof(StaffActivityActionType.MemberCheckIn)),
            GuestCheckInCount = ordered.Count(r => r.ActionTypeKey == nameof(StaffActivityActionType.GuestCheckIn)),
            EquipmentPickupCount = ordered.Count(r => r.ActionTypeKey == nameof(StaffActivityActionType.EquipmentPickup))
        };
    }

    /// <summary>
    /// Aggregates booking, rental, facility, and member activity charts for the admin/staff home dashboard.
    /// Uses the last <paramref name="windowDays"/> days (UTC) ending now.
    /// </summary>
    public async Task<AdminHomeDashboardViewModel> GetAdminHomeDashboardAsync(
        int windowDays = 90,
        CancellationToken ct = default)
    {
        windowDays = Math.Clamp(windowDays, 7, 365);
        var endUtc = DateTime.UtcNow;
        var startUtc = endUtc.Date.AddDays(-(windowDays - 1));

        var bookingsInWindow = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.CreatedAt >= startUtc && b.CreatedAt <= endUtc)
            .Select(b => new { b.CreatedAt, b.Status, b.FacilityId, FacilityName = b.Facility!.Name, b.MemberId, MemberName = b.Member!.FullName })
            .ToListAsync(ct);

        var rentalsInWindow = await _db.EquipmentRentals
            .AsNoTracking()
            .Where(r => r.CreatedAt >= startUtc && r.CreatedAt <= endUtc
                        && r.Status != EquipmentRentalStatus.Cancelled
                        && r.Status != EquipmentRentalStatus.Invalid)
            .Select(r => new
            {
                EquipmentName = r.InventoryItem != null ? r.InventoryItem.Name : "(unknown)",
                Quantity = r.Quantity
            })
            .ToListAsync(ct);

        var checkInsInWindow = await _db.MemberCheckIns
            .AsNoTracking()
            .Where(c => c.CheckedInAt >= startUtc && c.CheckedInAt <= endUtc)
            .Select(c => new { c.MemberId, MemberName = c.Member!.FullName })
            .ToListAsync(ct);

        var bookingsByDayLookup = bookingsInWindow
            .GroupBy(b => b.CreatedAt.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var bookingsByDay = new List<NamedCountStat>();
        for (var day = startUtc.Date; day <= endUtc.Date; day = day.AddDays(1))
        {
            bookingsByDay.Add(new NamedCountStat
            {
                Label = day.ToString("dd MMM"),
                Count = bookingsByDayLookup.GetValueOrDefault(day)
            });
        }

        // Collapse dense daily bars for long windows: weekly buckets when > 45 days.
        if (windowDays > 45)
        {
            bookingsByDay = bookingsInWindow
                .GroupBy(b =>
                {
                    var d = b.CreatedAt.Date;
                    var offset = (int)(d - startUtc.Date).TotalDays;
                    var weekStart = startUtc.Date.AddDays(offset - (offset % 7));
                    return weekStart;
                })
                .OrderBy(g => g.Key)
                .Select(g => new NamedCountStat
                {
                    Label = g.Key.ToString("dd MMM"),
                    Count = g.Count()
                })
                .ToList();
        }

        var bookingsByStatus = bookingsInWindow
            .GroupBy(b => b.Status.ToString())
            .OrderByDescending(g => g.Count())
            .Select(g => new NamedCountStat { Label = g.Key, Count = g.Count() })
            .ToList();

        var rentalsByEquipment = rentalsInWindow
            .GroupBy(r => r.EquipmentName)
            .Select(g => new NamedCountStat
            {
                Label = g.Key,
                Count = g.Sum(x => Math.Max(1, x.Quantity))
            })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();

        var facilityUsage = bookingsInWindow
            .Where(b => b.Status != BookingStatus.Cancelled)
            .GroupBy(b => b.FacilityName)
            .Select(g => new NamedCountStat { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();

        var bookingCountsByMember = bookingsInWindow
            .Where(b => b.Status != BookingStatus.Cancelled)
            .GroupBy(b => new { b.MemberId, b.MemberName })
            .ToDictionary(g => g.Key.MemberId, g => (Name: g.Key.MemberName, Count: g.Count()));

        var checkInCountsByMember = checkInsInWindow
            .GroupBy(c => new { c.MemberId, c.MemberName })
            .ToDictionary(g => g.Key.MemberId, g => (Name: g.Key.MemberName, Count: g.Count()));

        var memberIds = bookingCountsByMember.Keys.Union(checkInCountsByMember.Keys);
        var topMembers = memberIds
            .Select(id =>
            {
                bookingCountsByMember.TryGetValue(id, out var bookings);
                checkInCountsByMember.TryGetValue(id, out var checkIns);
                return new TopMemberStat
                {
                    MemberName = !string.IsNullOrWhiteSpace(bookings.Name)
                        ? bookings.Name
                        : (!string.IsNullOrWhiteSpace(checkIns.Name) ? checkIns.Name : "(unknown)"),
                    BookingCount = bookings.Count,
                    CheckInCount = checkIns.Count
                };
            })
            .OrderByDescending(m => m.TotalActivity)
            .ThenByDescending(m => m.BookingCount)
            .Take(8)
            .ToList();

        var activeFacilities = await _db.Facilities.CountAsync(f => f.IsActive, ct);

        return new AdminHomeDashboardViewModel
        {
            WindowDays = windowDays,
            WindowStartUtc = startUtc,
            WindowEndUtc = endUtc,
            BookingsInWindow = bookingsInWindow.Count,
            RentalsInWindow = rentalsInWindow.Count,
            CheckInsInWindow = checkInsInWindow.Count,
            ActiveFacilities = activeFacilities,
            BookingsByDay = bookingsByDay,
            BookingsByStatus = bookingsByStatus,
            RentalsByEquipment = rentalsByEquipment,
            FacilityUsage = facilityUsage,
            TopMembers = topMembers
        };
    }

    private async Task<List<SelectListItem>> BuildStaffOptionsAsync(string? selectedStaffId, CancellationToken ct)
    {
        var staff = await _userManager.GetUsersInRoleAsync(AppRoles.Staff);
        var admins = await _userManager.GetUsersInRoleAsync(AppRoles.Administrator);
        var people = staff.Concat(admins)
            .GroupBy(u => u.Id)
            .Select(g => g.First())
            .OrderBy(u => u.FullName)
            .ToList();

        var options = new List<SelectListItem>
        {
            new("All staff / admin", "") { Selected = string.IsNullOrWhiteSpace(selectedStaffId) }
        };
        options.AddRange(people.Select(u => new SelectListItem(u.FullName, u.Id)
        {
            Selected = string.Equals(u.Id, selectedStaffId, StringComparison.Ordinal)
        }));
        return options;
    }

    private static bool MatchesMemberQuery(string? query, string displayName, string? memberId)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return displayName.Contains(query, StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrWhiteSpace(memberId) &&
                   memberId.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveActorName(string? storedName, string? userId)
    {
        if (!string.IsNullOrWhiteSpace(storedName))
        {
            return storedName.Trim();
        }

        return string.Empty;
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
