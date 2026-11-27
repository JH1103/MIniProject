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
        await EnsureSubscriptionSchemaAsync(db);
        await EnsureMemberCheckInTableAsync(db);
        await EnsureClearanceSchemaAsync(db);
        await EnsureEquipmentRentalTableAsync(db);
        await EnsureEquipmentRentalPickupSchemaAsync(db);
        await EnsureEquipmentCatalogTableAsync(db);

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

        await SeedMembershipPlansAsync(db);
        await SeedKotaPermaiFacilitiesAsync(db);
        await SeedEquipmentCatalogAsync(db);
        await EnsureFacilityInventoryAsync(db);
        await BackfillEquipmentCatalogAsync(db);

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
            planName: null);

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
            planName: null);

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

    private static async Task SeedMembershipPlansAsync(ApplicationDbContext db)
    {
        // Fees aligned with Kota Permai published membership figures
        // (approx. RM7,000 yearly renewal / RM35,000 individual joining).
        var plans = new[]
        {
            new MembershipPlan
            {
                Name = "Individual",
                Description = "One-year renewable individual membership with sports & recreation booking privileges (based on Kota Permai yearly membership).",
                AnnualFee = 7000m,
                AdvanceBookingDays = 14,
                MaxGuestsPerBooking = 2,
                DiscountPercent = 0,
                IsActive = true
            },
            new MembershipPlan
            {
                Name = "Family",
                Description = "Household membership covering linked family accounts, higher guest limits and facility discounts.",
                AnnualFee = 12500m,
                AdvanceBookingDays = 21,
                MaxGuestsPerBooking = 6,
                DiscountPercent = 10,
                IsActive = true
            },
            new MembershipPlan
            {
                Name = "Premium",
                Description = "Full golf & country club membership with priority booking and best facility discounts (aligned with Kota Permai individual joining fee).",
                AnnualFee = 35000m,
                AdvanceBookingDays = 30,
                MaxGuestsPerBooking = 10,
                DiscountPercent = 20,
                IsActive = true
            }
        };

        var existing = await db.MembershipPlans.ToListAsync();
        if (!existing.Any())
        {
            db.MembershipPlans.AddRange(plans);
            await db.SaveChangesAsync();
            return;
        }

        foreach (var plan in plans)
        {
            var match = existing.FirstOrDefault(p => p.Name == plan.Name);
            if (match is null)
            {
                db.MembershipPlans.Add(plan);
                continue;
            }

            match.Description = plan.Description;
            match.AnnualFee = plan.AnnualFee;
            match.AdvanceBookingDays = plan.AdvanceBookingDays;
            match.MaxGuestsPerBooking = plan.MaxGuestsPerBooking;
            match.DiscountPercent = plan.DiscountPercent;
            match.IsActive = true;
        }

        await db.SaveChangesAsync();
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
            HourlyRate = 1200m,
            OpenTime = new TimeSpan(9, 0, 0),
            CloseTime = new TimeSpan(23, 0, 0),
            SlotDurationMinutes = 120,
            BookingMode = BookingMode.ApprovalRequired,
            InventoryItems =
            [
                new InventoryItem { Name = "Round Tables", Quantity = 40, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Banquet Chairs", Quantity = 350, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Cocktail Tables", Quantity = 20, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Table Cloths", Quantity = 40, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Chair Covers", Quantity = 350, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "LCD Projector & Screen", Quantity = 2, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "PA System & Wireless Mics", Quantity = 1, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Lectern / Podium", Quantity = 2, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Stage Risers", Quantity = 6, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Danau Function Room",
            Category = "Meeting",
            Description = "Flexible function rooms (Danau 1–4) for meetings, classroom sessions and smaller receptions with golf-course views.",
            ImageUrl = "/images/facility-function-room.jpg",
            Capacity = 100,
            HourlyRate = 380m,
            OpenTime = new TimeSpan(8, 0, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.ApprovalRequired,
            InventoryItems =
            [
                new InventoryItem { Name = "Conference Tables", Quantity = 12, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Banquet Chairs", Quantity = 100, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Whiteboard", Quantity = 2, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Flip Chart Stand", Quantity = 2, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Projector", Quantity = 1, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "LCD Projector & Screen", Quantity = 1, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "PA System & Wireless Mics", Quantity = 1, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "HDMI Cable Kit", Quantity = 2, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Extension Cord / Power Strip", Quantity = 4, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "VIP Room",
            Category = "Meeting",
            Description = "Private VIP rooms for small meetings or golf day hosts (rooms for 2–4 persons available).",
            ImageUrl = "/images/facility-vip-room.jpg",
            Capacity = 4,
            HourlyRate = 180m,
            OpenTime = new TimeSpan(8, 0, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Conference Tables", Quantity = 1, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Whiteboard", Quantity = 1, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "HDMI Cable Kit", Quantity = 1, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Indoor Badminton Court",
            Category = "Sports",
            Description = "One of eight indoor badminton courts with non-marking flooring. Racket rental available at the sports counter.",
            ImageUrl = "/images/facility-badminton.jpg",
            Capacity = 8,
            HourlyRate = 28m,
            OpenTime = new TimeSpan(7, 0, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Badminton Rackets", Quantity = 16, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Shuttlecocks (tube)", Quantity = 30, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Non-marking Court Shoes", Quantity = 10, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Tennis Court",
            Category = "Sports",
            Description = "Fully-lit outdoor tennis court among three courts available for daily member use.",
            ImageUrl = "/images/facility-tennis.jpg",
            Capacity = 4,
            HourlyRate = 45m,
            OpenTime = new TimeSpan(7, 0, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Tennis Rackets", Quantity = 8, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Tennis Balls (can)", Quantity = 20, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Squash Court",
            Category = "Sports",
            Description = "Indoor squash court with glass back wall. Two courts available at the sports complex.",
            ImageUrl = "/images/facility-squash.jpg",
            Capacity = 2,
            HourlyRate = 22m,
            OpenTime = new TimeSpan(7, 0, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            SlotDurationMinutes = 45,
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Squash Rackets", Quantity = 8, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Squash Balls", Quantity = 24, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Protective Eyewear", Quantity = 6, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Pickleball Court",
            Category = "Sports",
            Description = "Outdoor pickleball court for members and guests — book a slot at the sports counter online.",
            ImageUrl = "/images/facility-pickleball.jpg",
            Capacity = 4,
            HourlyRate = 25m,
            OpenTime = new TimeSpan(7, 0, 0),
            CloseTime = new TimeSpan(21, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Pickleball Paddles", Quantity = 12, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Pickleball Balls", Quantity = 30, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Table Tennis",
            Category = "Sports",
            Description = "Table tennis tables available for casual play and practice sessions.",
            ImageUrl = "/images/facility-table-tennis.jpg",
            Capacity = 4,
            HourlyRate = 12m,
            OpenTime = new TimeSpan(8, 0, 0),
            CloseTime = new TimeSpan(21, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Table Tennis Bats", Quantity = 12, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Table Tennis Balls", Quantity = 40, Condition = InventoryCondition.Good }
            ]
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
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Kickboards", Quantity = 20, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Pool Noodles", Quantity = 15, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Swim Fins", Quantity = 12, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Pull Buoys", Quantity = 16, Condition = InventoryCondition.Good }
            ]
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
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Yoga Mats", Quantity = 20, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Dumbbells (pair)", Quantity = 15, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Resistance Bands", Quantity = 20, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Foam Rollers", Quantity = 10, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Jump Ropes", Quantity = 12, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Driving Range Bay",
            Category = "Golf",
            Description = "60-bay, two-tier all-weather driving range with premium mats and Srixon range balls. Practice green, chipping and bunker areas available.",
            ImageUrl = "/images/facility-driving-range.jpg",
            Capacity = 2,
            HourlyRate = 48m,
            OpenTime = new TimeSpan(7, 0, 0),
            CloseTime = new TimeSpan(21, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Range Ball Baskets", Quantity = 60, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Golf Clubs (set)", Quantity = 8, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Golf Push Trolley", Quantity = 6, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Golf Umbrella", Quantity = 10, Condition = InventoryCondition.Good }
            ]
        },
        new Facility
        {
            Name = "Multi-Purpose Room",
            Category = "Leisure",
            Description = "Activity room for yoga, zumba, line dance and other member programmes organised at Kota Permai.",
            ImageUrl = "/images/facility-multipurpose.jpg",
            Capacity = 30,
            HourlyRate = 150m,
            OpenTime = new TimeSpan(8, 0, 0),
            CloseTime = new TimeSpan(21, 0, 0),
            SlotDurationMinutes = 60,
            BookingMode = BookingMode.InstantConfirmation,
            InventoryItems =
            [
                new InventoryItem { Name = "Yoga Mats", Quantity = 25, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Bluetooth Speaker", Quantity = 2, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Resistance Bands", Quantity = 15, Condition = InventoryCondition.Good },
                new InventoryItem { Name = "Foam Rollers", Quantity = 8, Condition = InventoryCondition.Good }
            ]
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

        // Staff/Admin work accounts must not hold personal membership plans.
        if (role is AppRoles.Administrator or AppRoles.Staff || planName is null)
        {
            var workSubs = await db.Subscriptions
                .Where(s => s.UserId == user.Id &&
                            (s.Status == MembershipStatus.Active || s.Status == MembershipStatus.Suspended) &&
                            s.CancelledAt == null)
                .ToListAsync();
            if (workSubs.Count > 0)
            {
                foreach (var sub in workSubs)
                {
                    sub.AutoRenew = false;
                    sub.CancelledAt = DateTime.UtcNow;
                    sub.Status = MembershipStatus.Expired;
                    sub.EndDate = DateTime.UtcNow.Date;
                }
            }

            if (user.MembershipStatus != MembershipStatus.Expired)
            {
                user.MembershipStatus = MembershipStatus.Expired;
                await userManager.UpdateAsync(user);
            }

            if (workSubs.Count > 0)
            {
                await db.SaveChangesAsync();
            }

            return;
        }

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
                Status = MembershipStatus.Active,
                AutoRenew = true
            });
            user.MembershipStatus = MembershipStatus.Active;
            await userManager.UpdateAsync(user);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// EnsureCreated does not add new columns to an existing SQL Server database.
    /// Keep Subscription.AutoRenew / CancelledAt available for upgraded installs.
    /// </summary>
    private static async Task EnsureSubscriptionSchemaAsync(ApplicationDbContext db)
    {
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('Subscriptions', 'AutoRenew') IS NULL
                ALTER TABLE [Subscriptions] ADD [AutoRenew] bit NOT NULL CONSTRAINT DF_Subscriptions_AutoRenew DEFAULT(0);
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('Subscriptions', 'CancelledAt') IS NULL
                ALTER TABLE [Subscriptions] ADD [CancelledAt] datetime2 NULL;
            """);
    }

    private static async Task EnsureMemberCheckInTableAsync(ApplicationDbContext db)
    {
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[MemberCheckIns]', N'U') IS NULL
            BEGIN
                CREATE TABLE [MemberCheckIns] (
                    [Id] int NOT NULL IDENTITY,
                    [MemberId] nvarchar(450) NOT NULL,
                    [BookingId] int NULL,
                    [FacilityId] int NULL,
                    [CheckedInAt] datetime2 NOT NULL,
                    [CheckedOutAt] datetime2 NULL,
                    [Status] int NOT NULL CONSTRAINT DF_MemberCheckIns_Status DEFAULT(0),
                    [CheckedInByUserId] nvarchar(max) NULL,
                    [CheckedOutByUserId] nvarchar(max) NULL,
                    CONSTRAINT [PK_MemberCheckIns] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_MemberCheckIns_AspNetUsers_MemberId]
                        FOREIGN KEY ([MemberId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_MemberCheckIns_MemberId_CheckedOutAt]
                    ON [MemberCheckIns] ([MemberId], [CheckedOutAt]);
            END
            """);
    }

    /// <summary>
    /// Adds booking/facility/status columns for clearance desk on existing installs.
    /// </summary>
    private static async Task EnsureClearanceSchemaAsync(ApplicationDbContext db)
    {
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('MemberCheckIns', 'BookingId') IS NULL
                ALTER TABLE [MemberCheckIns] ADD [BookingId] int NULL;
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('MemberCheckIns', 'FacilityId') IS NULL
                ALTER TABLE [MemberCheckIns] ADD [FacilityId] int NULL;
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('MemberCheckIns', 'Status') IS NULL
                ALTER TABLE [MemberCheckIns] ADD [Status] int NOT NULL CONSTRAINT DF_MemberCheckIns_Status_Col DEFAULT(0);
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('CheckInRecords', 'Status') IS NULL
                ALTER TABLE [CheckInRecords] ADD [Status] int NOT NULL CONSTRAINT DF_CheckInRecords_Status DEFAULT(0);
            """);
    }

    private static async Task EnsureEquipmentRentalTableAsync(ApplicationDbContext db)
    {
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[EquipmentRentals]', N'U') IS NULL
            BEGIN
                CREATE TABLE [EquipmentRentals] (
                    [Id] int NOT NULL IDENTITY,
                    [MemberId] nvarchar(450) NOT NULL,
                    [InventoryItemId] int NOT NULL,
                    [BookingId] int NULL,
                    [Quantity] int NOT NULL,
                    [StartUtc] datetime2 NOT NULL,
                    [DueUtc] datetime2 NOT NULL,
                    [PickedUpAt] datetime2 NULL,
                    [ReturnedAt] datetime2 NULL,
                    [Status] int NOT NULL,
                    [Fee] decimal(18,2) NOT NULL,
                    [Notes] nvarchar(500) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [PK_EquipmentRentals] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_EquipmentRentals_AspNetUsers_MemberId]
                        FOREIGN KEY ([MemberId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_EquipmentRentals_InventoryItems_InventoryItemId]
                        FOREIGN KEY ([InventoryItemId]) REFERENCES [InventoryItems] ([Id]) ON DELETE NO ACTION
                );
                CREATE INDEX [IX_EquipmentRentals_MemberId_Status]
                    ON [EquipmentRentals] ([MemberId], [Status]);
            END
            """);
    }

    /// <summary>
    /// Adds PickedUpAt for staff checkout confirmation on existing installs.
    /// </summary>
    private static async Task EnsureEquipmentRentalPickupSchemaAsync(ApplicationDbContext db)
    {
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('EquipmentRentals', 'PickedUpAt') IS NULL
                ALTER TABLE [EquipmentRentals] ADD [PickedUpAt] datetime2 NULL;
            """);
    }

    /// <summary>
    /// Creates the equipment catalog table on existing SQL Server installs
    /// (EnsureCreated does not add new tables to an already-created database).
    /// </summary>
    private static async Task EnsureEquipmentCatalogTableAsync(ApplicationDbContext db)
    {
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[EquipmentCatalogEntries]', N'U') IS NULL
            BEGIN
                CREATE TABLE [EquipmentCatalogEntries] (
                    [Id] int NOT NULL IDENTITY,
                    [Name] nvarchar(120) NOT NULL,
                    [Description] nvarchar(500) NULL,
                    [IsActive] bit NOT NULL CONSTRAINT DF_EquipmentCatalogEntries_IsActive DEFAULT(1),
                    CONSTRAINT [PK_EquipmentCatalogEntries] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_EquipmentCatalogEntries_Name]
                    ON [EquipmentCatalogEntries] ([Name]);
            END
            """);
    }

    /// <summary>
    /// Seeds the club-wide equipment catalog with typical Kota Permai facility
    /// rentables (sports, golf, pool, gym, banquet, meeting). Idempotent by name.
    /// </summary>
    private static async Task SeedEquipmentCatalogAsync(ApplicationDbContext db)
    {
        var desired = CreateEquipmentCatalogEntries();
        var existing = await db.EquipmentCatalogEntries.ToListAsync();
        var byName = existing.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in desired)
        {
            if (byName.TryGetValue(entry.Name, out var match))
            {
                if (string.IsNullOrWhiteSpace(match.Description) && !string.IsNullOrWhiteSpace(entry.Description))
                {
                    match.Description = entry.Description;
                }

                if (!match.IsActive)
                {
                    match.IsActive = true;
                }

                continue;
            }

            db.EquipmentCatalogEntries.Add(entry);
            byName[entry.Name] = entry;
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }
    }

    private static List<EquipmentCatalogEntry> CreateEquipmentCatalogEntries() =>
    [
        // Badminton
        new() { Name = "Badminton Rackets", Description = "Adult badminton racket rental from the sports counter." },
        new() { Name = "Shuttlecocks (tube)", Description = "Tube of feather or nylon shuttlecocks for court play." },
        new() { Name = "Non-marking Court Shoes", Description = "Indoor non-marking shoes for badminton and squash courts." },
        // Tennis
        new() { Name = "Tennis Rackets", Description = "Adult tennis racket rental for outdoor court bookings." },
        new() { Name = "Tennis Balls (can)", Description = "Can of pressure tennis balls for match or practice play." },
        // Squash
        new() { Name = "Squash Rackets", Description = "Squash racket rental for indoor court sessions." },
        new() { Name = "Squash Balls", Description = "Single or double yellow-dot squash balls." },
        new() { Name = "Protective Eyewear", Description = "Court-approved protective eyewear for squash." },
        // Pickleball
        new() { Name = "Pickleball Paddles", Description = "Composite pickleball paddles for outdoor court play." },
        new() { Name = "Pickleball Balls", Description = "Outdoor pickleball balls for member court bookings." },
        // Table tennis
        new() { Name = "Table Tennis Bats", Description = "Table tennis bats for casual and practice sessions." },
        new() { Name = "Table Tennis Balls", Description = "40mm table tennis balls (pack)." },
        // Pool / swimming
        new() { Name = "Kickboards", Description = "Foam kickboards for lap-pool training." },
        new() { Name = "Pool Noodles", Description = "Foam pool noodles for aqua fitness and learn-to-swim." },
        new() { Name = "Swim Fins", Description = "Short training swim fins (pair)." },
        new() { Name = "Pull Buoys", Description = "Pull buoys for stroke technique training." },
        // Gym / multi-purpose
        new() { Name = "Yoga Mats", Description = "Non-slip yoga / exercise mats for gym and activity classes." },
        new() { Name = "Dumbbells (pair)", Description = "Free-weight dumbbell pair for gym floor use." },
        new() { Name = "Resistance Bands", Description = "Loop or tube resistance bands for strength and rehab." },
        new() { Name = "Foam Rollers", Description = "Foam rollers for warm-up and recovery." },
        new() { Name = "Jump Ropes", Description = "Speed jump ropes for cardio warm-ups." },
        new() { Name = "Bluetooth Speaker", Description = "Portable Bluetooth speaker for multi-purpose room classes." },
        // Golf / driving range
        new() { Name = "Range Ball Baskets", Description = "Basket of range balls for driving-range bay hire." },
        new() { Name = "Golf Clubs (set)", Description = "Half or full rental set of golf clubs." },
        new() { Name = "Golf Push Trolley", Description = "Manual push trolley for practice or course use." },
        new() { Name = "Golf Umbrella", Description = "Large golf umbrella for wet-weather practice." },
        // Banquet / ballroom
        new() { Name = "Round Tables", Description = "Banquet round tables for ballroom and reception layouts." },
        new() { Name = "Banquet Chairs", Description = "Stackable banquet chairs for functions and meetings." },
        new() { Name = "Cocktail Tables", Description = "High cocktail / cabaret tables for receptions." },
        new() { Name = "Table Cloths", Description = "Fitted or drape table cloths for banquet tables." },
        new() { Name = "Chair Covers", Description = "Banquet chair covers for formal events." },
        new() { Name = "LCD Projector & Screen", Description = "LCD projector with portable projection screen." },
        new() { Name = "PA System & Wireless Mics", Description = "Portable PA system with wireless handheld microphones." },
        new() { Name = "Lectern / Podium", Description = "Freestanding lectern or podium for speeches." },
        new() { Name = "Stage Risers", Description = "Modular stage riser platforms for ballroom stages." },
        // Meeting / function
        new() { Name = "Conference Tables", Description = "Modular conference tables for meeting layouts." },
        new() { Name = "Whiteboard", Description = "Mobile whiteboard with markers for workshops." },
        new() { Name = "Flip Chart Stand", Description = "Flip-chart easel with paper pad." },
        new() { Name = "Projector", Description = "Meeting-room projector for presentations." },
        new() { Name = "HDMI Cable Kit", Description = "HDMI / adapter cable kit for laptop presentations." },
        new() { Name = "Extension Cord / Power Strip", Description = "Power strip and extension lead for AV setups." }
    ];

    /// <summary>
    /// Ensures each seeded facility has its expected rentable inventory rows
    /// (adds missing names only; does not overwrite existing quantities).
    /// </summary>
    private static async Task EnsureFacilityInventoryAsync(ApplicationDbContext db)
    {
        var desiredByFacility = CreateKotaPermaiFacilities()
            .ToDictionary(f => f.Name, f => f.InventoryItems.ToList(), StringComparer.OrdinalIgnoreCase);

        var facilities = await db.Facilities.Include(f => f.InventoryItems).ToListAsync();
        foreach (var facility in facilities)
        {
            if (!desiredByFacility.TryGetValue(facility.Name, out var desiredItems) || desiredItems.Count == 0)
            {
                continue;
            }

            var existingNames = new HashSet<string>(
                facility.InventoryItems.Select(i => i.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in desiredItems)
            {
                if (existingNames.Contains(item.Name))
                {
                    continue;
                }

                db.InventoryItems.Add(new InventoryItem
                {
                    FacilityId = facility.Id,
                    Name = item.Name,
                    Quantity = item.Quantity,
                    Condition = item.Condition
                });
                existingNames.Add(item.Name);
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Backfills distinct inventory item names into the equipment catalog so
    /// facility inventory can pick from catalog entries.
    /// </summary>
    private static async Task BackfillEquipmentCatalogAsync(ApplicationDbContext db)
    {
        var existingNames = await db.EquipmentCatalogEntries
            .Select(e => e.Name)
            .ToListAsync();
        var existingSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        var inventoryNames = await db.InventoryItems
            .Select(i => i.Name)
            .Where(n => n != null && n != "")
            .Distinct()
            .ToListAsync();

        foreach (var name in inventoryNames)
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0 || existingSet.Contains(trimmed))
            {
                continue;
            }

            db.EquipmentCatalogEntries.Add(new EquipmentCatalogEntry
            {
                Name = trimmed,
                IsActive = true
            });
            existingSet.Add(trimmed);
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }
    }
}


