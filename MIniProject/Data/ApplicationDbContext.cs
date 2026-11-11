using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MIniProject.Models;

namespace MIniProject.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<FacilityBlockOut> FacilityBlockOuts => Set<FacilityBlockOut>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingGuest> BookingGuests => Set<BookingGuest>();
    public DbSet<WaitingListEntry> WaitingListEntries => Set<WaitingListEntry>();
    public DbSet<Guest> Guests => Set<Guest>();
    public DbSet<VisitorPass> VisitorPasses => Set<VisitorPass>();
    public DbSet<CheckInRecord> CheckInRecords => Set<CheckInRecord>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<RefundRequest> RefundRequests => Set<RefundRequest>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Facility>()
            .HasMany(f => f.InventoryItems)
            .WithOne(i => i.Facility!)
            .HasForeignKey(i => i.FacilityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Facility>()
            .HasMany(f => f.BlockOuts)
            .WithOne(b => b.Facility!)
            .HasForeignKey(b => b.FacilityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Booking>()
            .HasOne(b => b.Invoice)
            .WithOne(i => i.Booking!)
            .HasForeignKey<Invoice>(i => i.BookingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Booking>()
            .HasIndex(b => new { b.FacilityId, b.StartUtc, b.EndUtc });

        builder.Entity<Invoice>()
            .HasIndex(i => i.InvoiceNumber)
            .IsUnique();

        builder.Entity<VisitorPass>()
            .HasIndex(p => p.PassCode)
            .IsUnique();

        builder.Entity<SystemSetting>()
            .HasIndex(s => s.Key)
            .IsUnique();
    }
}
