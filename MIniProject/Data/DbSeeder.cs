using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MIniProject.Models;

namespace MIniProject.Data;

public static class DbSeeder
{
    public const string ClubName = "Kota Permai Golf & Country Club";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();

        await db.Database.EnsureCreatedAsync();

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        if (!db.SystemSettings.Any())
        {
            db.SystemSettings.AddRange(
                new SystemSetting { Key = "RefundFullHours", Value = "48" },
                new SystemSetting { Key = "RefundHalfHours", Value = "24" },
                new SystemSetting { Key = "ClubhouseName", Value = ClubName },
                new SystemSetting { Key = "Currency", Value = "MYR" }
            );
        }
        else
        {
            var nameSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "ClubhouseName");
            if (nameSetting is not null && nameSetting.Value != ClubName)
            {
                nameSetting.Value = ClubName;
            }
        }

        if (!db.MembershipPlans.Any())
        {
            db.MembershipPlans.AddRange(
                new MembershipPlan
                {
                    Name = "Individual",
                    Description = "Access for one adult member with standard booking privileges at Kota Permai.",
                    AnnualFee = 350m,
                    AdvanceBookingDays = 14,
                    MaxGuestsPerBooking = 2,
                    DiscountPercent = 0
                },
                new MembershipPlan
                {
                    Name = "Family",
                    Description = "Household membership with linked accounts and higher guest limits.",
                    AnnualFee = 650m,
                    AdvanceBookingDays = 21,
                    MaxGuestsPerBooking = 6,
                    DiscountPercent = 10
                },
                new MembershipPlan
                {
                    Name = "Premium",
                    Description = "Priority booking windows, highest guest allowance and facility discounts.",
                    AnnualFee = 980m,
                    AdvanceBookingDays = 30,
                    MaxGuestsPerBooking = 10,
                    DiscountPercent = 20
                }
            );
        }

        await SeedKotaPermaiFacilitiesAsync(db);

        if (!db.Announcements.Any())
        {
            db.Announcements.AddRange(
                new Announcement
                {
                    Title = "Online facility booking is live",
                    Body = "Kota Permai members can now reserve sports courts, pools, driving range bays, function rooms and more online."
                },
                new Announcement
                {
                    Title = "Lap pool morning lanes",
                    Body = "Olympic-sized lap pool lanes are reserved for members on weekdays from 6:30 AM to 8:00 AM. Please book ahead."
                },
                new Announcement
                {
                    Title = "Permai Ballroom bookings",
                    Body = "Banquet and wedding bookings for Permai Ballroom require staff approval. Submit your request online and our team will confirm."
                }
            );
        }
        else
        {
            // Refresh old Greenwood announcement wording if still present.
            foreach (var announcement in db.Announcements.Where(a => a.Body.Contains("Greenwood")))
            {
                announcement.Body = announcement.Body.Replace("Greenwood Clubhouse", ClubName);
            }
        }

        await db.SaveChangesAsync();

        await EnsureUserAsync(userManager, db,
            email: "admin@kotapermai.local",
            fullName: "System Administrator",
            password: "Admin@12345",
            role: AppRoles.Administrator,
            planName: "Premium");

        await EnsureUserAsync(userManager, db,
            email: "staff@kotapermai.local",
            fullName: "Clubhouse Staff",
            password: "Staff@12345",
            role: AppRoles.Staff,
            planName: null);

        await EnsureUserAsync(userManager, db,
            email: "member@kotapermai.local",
            fullName: "Aisha Rahman",
            password: "Member@12345",
            role: AppRoles.Member,
            planName: "Family");

        // Keep legacy demo emails working for existing docs/tests.
        await EnsureUserAsync(userManager, db,
            email: "admin@clubhouse.local",
            fullName: "System Administrator",
            password: "Admin@12345",
            role: AppRoles.Administrator,
            planName: "Premium");

        await EnsureUserAsync(userManager, db,
            email: "staff@clubhouse.local",
            fullName: "Clubhouse Staff",
            password: "Staff@12345",
            role: AppRoles.Staff,
            planName: null);

        await EnsureUserAsync(userManager, db,
            email: "member@clubhouse.local",
            fullName: "Aisha Rahman",
            password: "Member@12345",
            role: AppRoles.Member,
            planName: "Family");
    }

    private static async Task SeedKotaPermaiFacilitiesAsync(ApplicationDbContext db)
    {
        var desired = CreateKotaPermaiFacilities();
        var existing = await db.Facilities.Include(f => f.InventoryItems).ToListAsync();

        // Replace placeholder Greenwood facilities with Kota Permai catalogue.
        var legacyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Function Hall", "Badminton Court A", "Meeting Room", "Swimming Pool", "Barbecue Area"
        };

        var hasLegacyOnly = existing.Count > 0 && existing.All(f => legacyNames.Contains(f.Name));
        if (hasLegacyOnly)
        {
            db.InventoryItems.RemoveRange(existing.SelectMany(f => f.InventoryItems));
            db.Facilities.RemoveRange(existing);
            await db.SaveChangesAsync();
            existing.Clear();
        }

        if (!existing.Any())
        {
            db.Facilities.AddRange(desired);
            await db.SaveChangesAsync();
            return;
        }

        foreach (var facility in desired)
        {
            var match = existing.FirstOrDefault(f => f.Name == facility.Name);
            if (match is null)
            {
                db.Facilities.Add(facility);
                continue;
            }

            match.Category = facility.Category;
            match.Description = facility.Description;
            match.ImageUrl = facility.ImageUrl;
            match.Capacity = facility.Capacity;
            match.HourlyRate = facility.HourlyRate;
            match.OpenTime = facility.OpenTime;
            match.CloseTime = facility.CloseTime;
            match.SlotDurationMinutes = facility.SlotDurationMinutes;
            match.BookingMode = facility.BookingMode;
            match.Status = FacilityStatus.Available;
            match.IsActive = true;
            match.PaymentHoldMinutes = facility.PaymentHoldMinutes;
        }

        await db.SaveChangesAsync();
    }

    private static List<Facility> CreateKotaPermaiFacilities() =>
    [
        new Facility
        {
            Name = "Permai Ballroom",
            Category = "Banquet",
            Description = "Pillar-less flagship ballroom overlooking the golf course and pool, suitable for weddings, seminars and banquets for up to 350 guests.",
            ImageUrl = "/images/facility-ballroom.jpg",
            Capacity = 350,
            HourlyRate = 480m,
            OpenTime = new TimeSpan(9, 0, 0),
            CloseTime = new TimeSpan(23, 0, 0),
            SlotDurationMinutes = 120,
            BookingMode = BookingMode.ApprovalRequired,
            InventoryItems =
            [
                new InventoryItem { Name = "Round Tables", Quantity = 40, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Banquet Chairs", Quantity = 350, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "LCD Projector & Screen", Quantity = 2, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "PA System & Wireless Mics", Quantity = 1, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Danau Function Room",
            Category = "Meeting",
            Description = "Flexible function rooms (Danau 1–4) for meetings, classroom sessions and smaller receptions with golf-course views.",
            ImageUrl = "/images/facility-function-room.jpg",
            Capacity = 100,
            HourlyRate = 160m,
            OpenTime = new TimeSpan(8, 0, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.ApprovalRequired,
            InventoryItems =
            [
                new InventoryItem { Name = "Conference Tables", Quantity = 12, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Whiteboard", Quantity = 2, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Projector", Quantity = 1, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "VIP Room",
            Category = "Meeting",
            Description = "Private VIP rooms for small meetings or golf day hosts (rooms for 2–4 persons available).",
            ImageUrl = "/images/facility-vip-room.jpg",
            Capacity = 4,
            HourlyRate = 90m,
            OpenTime = new TimeSpan(8, 0, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation
        },
        new Facility
        {
            Name = "Indoor Badminton Court",
            Category = "Sports",
            Description = "One of eight indoor badminton courts with non-marking flooring. Racket rental available at the sports counter.",
            ImageUrl = "/images/facility-badminton.jpg",
            Capacity = 8,
            HourlyRate = 35m,
            OpenTime = new TimeSpan(7, 0, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Badminton Rackets", Quantity = 16, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Shuttlecocks (tube)", Quantity = 30, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Tennis Court",
            Category = "Sports",
            Description = "Fully-lit outdoor tennis court among three courts available for daily member use.",
            ImageUrl = "/images/facility-tennis.jpg",
            Capacity = 4,
            HourlyRate = 40m,
            OpenTime = new TimeSpan(7, 0, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Tennis Rackets", Quantity = 8, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Squash Court",
            Category = "Sports",
            Description = "Indoor squash court with glass back wall. Two courts available at the sports complex.",
            ImageUrl = "/images/facility-squash.jpg",
            Capacity = 2,
            HourlyRate = 30m,
            OpenTime = new TimeSpan(7, 0, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            SlotDurationMinutes = 45,
            BookingMode = BookingMode.InstantConfirmation
        },
        new Facility
        {
            Name = "Pickleball Court",
            Category = "Sports",
            Description = "Outdoor pickleball court for members and guests — book a slot at the sports counter online.",
            ImageUrl = "/images/facility-pickleball.jpg",
            Capacity = 4,
            HourlyRate = 30m,
            OpenTime = new TimeSpan(7, 0, 0),
            CloseTime = new TimeSpan(21, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation
        },
        new Facility
        {
            Name = "Table Tennis",
            Category = "Sports",
            Description = "Table tennis tables available for casual play and practice sessions.",
            ImageUrl = "/images/facility-table-tennis.jpg",
            Capacity = 4,
            HourlyRate = 15m,
            OpenTime = new TimeSpan(8, 0, 0),
            CloseTime = new TimeSpan(21, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation
        },
        new Facility
        {
            Name = "Olympic Lap Pool",
            Category = "Sports",
            Description = "Olympic-sized lap pool for member swimming sessions. Fun pool and children pool are nearby for families.",
            ImageUrl = "/images/facility-swimming-pool.jpg",
            Capacity = 40,
            HourlyRate = 0m,
            OpenTime = new TimeSpan(6, 30, 0),
            CloseTime = new TimeSpan(21, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation
        },
        new Facility
        {
            Name = "Gymnasium",
            Category = "Sports",
            Description = "Two-tier fully equipped gym with cardio and strength equipment. Sauna and steam bath available in the wellness area.",
            ImageUrl = "/images/facility-gym.jpg",
            Capacity = 25,
            HourlyRate = 0m,
            OpenTime = new TimeSpan(6, 0, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation
        },
        new Facility
        {
            Name = "Driving Range Bay",
            Category = "Golf",
            Description = "60-bay, two-tier all-weather driving range with premium mats and Srixon range balls. Practice green, chipping and bunker areas available.",
            ImageUrl = "/images/facility-driving-range.jpg",
            Capacity = 2,
            HourlyRate = 25m,
            OpenTime = new TimeSpan(7, 0, 0),
            CloseTime = new TimeSpan(21, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Range Ball Baskets", Quantity = 60, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Multi-Purpose Room",
            Category = "Leisure",
            Description = "Activity room for yoga, zumba, line dance and other member programmes organised at Kota Permai.",
            ImageUrl = "/images/facility-multipurpose.jpg",
            Capacity = 30,
            HourlyRate = 70m,
            OpenTime = new TimeSpan(8, 0, 0),
            CloseTime = new TimeSpan(21, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation
        }
    ];

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        string email,
        string fullName,
        string password,
        string role,
        string? planName)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                PdpaConsent = true,
                PdpaConsentAt = DateTime.UtcNow,
                MembershipStatus = planName is null ? MembershipStatus.Expired : MembershipStatus.Active,
                PhoneNumber = "0123456789"
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }

        if (planName is not null)
        {
            var plan = await db.MembershipPlans.FirstAsync(p => p.Name == planName);
            var hasActive = await db.Subscriptions.AnyAsync(s =>
                s.UserId == user.Id &&
                s.Status == MembershipStatus.Active &&
                s.EndDate >= DateTime.UtcNow);

            if (!hasActive)
            {
                db.Subscriptions.Add(new Subscription
                {
                    UserId = user.Id,
                    MembershipPlanId = plan.Id,
                    StartDate = DateTime.UtcNow.Date,
                    EndDate = DateTime.UtcNow.Date.AddYears(1),
                    Status = MembershipStatus.Active
                });
                user.MembershipStatus = MembershipStatus.Active;
                await userManager.UpdateAsync(user);
                await db.SaveChangesAsync();
            }
        }
    }
}
