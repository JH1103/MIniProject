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
    public DbSet<EquipmentRental> EquipmentRentals => Set<EquipmentRental>();
    public DbSet<FacilityBlockOut> FacilityBlockOuts => Set<FacilityBlockOut>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingGuest> BookingGuests => Set<BookingGuest>();
    public DbSet<WaitingListEntry> WaitingListEntries => Set<WaitingListEntry>();
    public DbSet<Guest> Guests => Set<Guest>();
    public DbSet<VisitorPass> VisitorPasses => Set<VisitorPass>();
    public DbSet<CheckInRecord> CheckInRecords => Set<CheckInRecord>();
    public DbSet<MemberCheckIn> MemberCheckIns => Set<MemberCheckIn>();
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

        // SQL Server rejects multiple cascade paths (SQLite allowed them).
        builder.Entity<Booking>()
            .HasOne(b => b.Member)
            .WithMany(u => u.Bookings)
            .HasForeignKey(b => b.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Booking>()
            .HasOne(b => b.Facility)
            .WithMany(f => f.Bookings)
            .HasForeignKey(b => b.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<BookingGuest>()
            .HasOne(bg => bg.Booking)
            .WithMany(b => b.Guests)
            .HasForeignKey(bg => bg.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<BookingGuest>()
            .HasOne(bg => bg.Guest)
            .WithMany(g => g.BookingGuests)
            .HasForeignKey(bg => bg.GuestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Guest>()
            .HasOne(g => g.HostMember)
            .WithMany(u => u.HostedGuests)
            .HasForeignKey(g => g.HostMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<VisitorPass>()
            .HasOne(p => p.Guest)
            .WithMany(g => g.Passes)
            .HasForeignKey(p => p.GuestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<VisitorPass>()
            .HasOne(p => p.Booking)
            .WithMany()
            .HasForeignKey(p => p.BookingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<CheckInRecord>()
            .HasOne(c => c.VisitorPass)
            .WithMany(p => p.CheckIns)
            .HasForeignKey(c => c.VisitorPassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Subscription>()
            .HasOne(s => s.User)
            .WithMany(u => u.Subscriptions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Subscription>()
            .HasOne(s => s.MembershipPlan)
            .WithMany(p => p.Subscriptions)
            .HasForeignKey(s => s.MembershipPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Invoice>()
            .HasOne(i => i.User)
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Invoice>()
            .HasOne(i => i.Subscription)
            .WithMany()
            .HasForeignKey(i => i.SubscriptionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<WaitingListEntry>()
            .HasOne(w => w.Member)
            .WithMany()
            .HasForeignKey(w => w.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<WaitingListEntry>()
            .HasOne(w => w.Facility)
            .WithMany()
            .HasForeignKey(w => w.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Booking>()
            .HasIndex(b => new { b.FacilityId, b.StartUtc, b.EndUtc });

        builder.Entity<Invoice>()
            .HasIndex(i => i.InvoiceNumber)
            .IsUnique();

        builder.Entity<VisitorPass>()
            .HasIndex(p => p.PassCode)
            .IsUnique();

        builder.Entity<MemberCheckIn>()
            .HasOne(c => c.Member)
            .WithMany()
            .HasForeignKey(c => c.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MemberCheckIn>()
            .HasOne(c => c.Booking)
            .WithMany()
            .HasForeignKey(c => c.BookingId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<MemberCheckIn>()
            .HasOne(c => c.Facility)
            .WithMany()
            .HasForeignKey(c => c.FacilityId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<MemberCheckIn>()
            .HasIndex(c => new { c.MemberId, c.CheckedOutAt });

        builder.Entity<EquipmentRental>()
            .HasOne(r => r.Member)
            .WithMany()
            .HasForeignKey(r => r.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<EquipmentRental>()
            .HasOne(r => r.InventoryItem)
            .WithMany()
            .HasForeignKey(r => r.InventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<EquipmentRental>()
            .HasOne(r => r.Booking)
            .WithMany()
            .HasForeignKey(r => r.BookingId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<EquipmentRental>()
            .HasIndex(r => new { r.MemberId, r.Status });

        builder.Entity<SystemSetting>()
            .HasIndex(s => s.Key)
            .IsUnique();
    }
}

