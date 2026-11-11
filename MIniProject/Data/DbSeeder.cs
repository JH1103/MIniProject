using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MIniProject.Models;

namespace MIniProject.Data;

public static class DbSeeder
{
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
                new SystemSetting { Key = "ClubhouseName", Value = "Greenwood Clubhouse" },
                new SystemSetting { Key = "Currency", Value = "MYR" }
            );
        }

        if (!db.MembershipPlans.Any())
        {
            db.MembershipPlans.AddRange(
                new MembershipPlan
                {
                    Name = "Individual",
                    Description = "Access for one adult member with standard booking privileges.",
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

        if (!db.Facilities.Any())
        {
            var hall = new Facility
            {
                Name = "Function Hall",
                Category = "Event",
                Description = "Air-conditioned multi-purpose hall for celebrations, meetings and private events.",
                ImageUrl = "/images/function-hall.svg",
                Capacity = 120,
                HourlyRate = 180m,
                OpenTime = new TimeSpan(9, 0, 0),
                CloseTime = new TimeSpan(23, 0, 0),
                SlotDurationMinutes = 120,
                BookingMode = BookingMode.ApprovalRequired,
                InventoryItems =
                [
                    new InventoryItem { Name = "Round Tables", Quantity = 15, Condition = InventoryCondition.Good },
                    new InventoryItem { Name = "Chairs", Quantity = 120, Condition = InventoryCondition.Good },
                    new InventoryItem { Name = "Projector & Screen", Quantity = 1, Condition = InventoryCondition.Good },
                    new InventoryItem { Name = "Sound System", Quantity = 1, Condition = InventoryCondition.Fair }
                ]
            };

            var badminton = new Facility
            {
                Name = "Badminton Court A",
                Category = "Sports",
                Description = "Indoor badminton court with non-marking flooring and racket rental available.",
                ImageUrl = "/images/badminton.svg",
                Capacity = 8,
                HourlyRate = 35m,
                OpenTime = new TimeSpan(7, 0, 0),
                CloseTime = new TimeSpan(22, 0, 0),
                SlotDurationMinutes = 60,
                BookingMode = BookingMode.InstantConfirmation,
                InventoryItems =
                [
                    new InventoryItem { Name = "Badminton Rackets", Quantity = 8, Condition = InventoryCondition.Good },
                    new InventoryItem { Name = "Shuttlecocks (tube)", Quantity = 20, Condition = InventoryCondition.Good }
                ]
            };

            var meeting = new Facility
            {
                Name = "Meeting Room",
                Category = "Meeting",
                Description = "Boardroom-style meeting space with whiteboard and video conference setup.",
                ImageUrl = "/images/meeting-room.svg",
                Capacity = 12,
                HourlyRate = 45m,
                OpenTime = new TimeSpan(8, 0, 0),
                CloseTime = new TimeSpan(20, 0, 0),
                SlotDurationMinutes = 60,
                BookingMode = BookingMode.InstantConfirmation,
                InventoryItems =
                [
                    new InventoryItem { Name = "Whiteboard Markers", Quantity = 10, Condition = InventoryCondition.Good },
                    new InventoryItem { Name = "Conference Camera", Quantity = 1, Condition = InventoryCondition.Good }
                ]
            };

            var pool = new Facility
            {
                Name = "Swimming Pool",
                Category = "Sports",
                Description = "25-metre community pool with dedicated lap lanes and family swim sessions.",
                ImageUrl = "/images/swimming-pool.svg",
                Capacity = 40,
                HourlyRate = 0m,
                OpenTime = new TimeSpan(6, 30, 0),
                CloseTime = new TimeSpan(21, 0, 0),
                SlotDurationMinutes = 60,
                BookingMode = BookingMode.InstantConfirmation
            };

            var bbq = new Facility
            {
                Name = "Barbecue Area",
                Category = "Leisure",
                Description = "Outdoor BBQ pits with seating, wash stations and rental equipment.",
                ImageUrl = "/images/bbq.svg",
                Capacity = 20,
                HourlyRate = 55m,
                OpenTime = new TimeSpan(10, 0, 0),
                CloseTime = new TimeSpan(22, 0, 0),
                SlotDurationMinutes = 120,
                BookingMode = BookingMode.InstantConfirmation,
                InventoryItems =
                [
                    new InventoryItem { Name = "BBQ Grill Sets", Quantity = 4, Condition = InventoryCondition.Good },
                    new InventoryItem { Name = "Charcoal Scoops", Quantity = 6, Condition = InventoryCondition.Fair }
                ]
            };

            db.Facilities.AddRange(hall, badminton, meeting, pool, bbq);
        }

        if (!db.Announcements.Any())
        {
            db.Announcements.AddRange(
                new Announcement
                {
                    Title = "Online booking is now live",
                    Body = "Members can reserve facilities, register guests and pay online through the Greenwood Clubhouse portal."
                },
                new Announcement
                {
                    Title = "Pool lane schedule",
                    Body = "Lap lanes are reserved on weekdays from 6:30 AM to 8:00 AM. Please book your preferred slot ahead of time."
                }
            );
        }

        await db.SaveChangesAsync();

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
