using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MIniProject.Models;

public class Invoice
{
    public int Id { get; set; }

    [Required, StringLength(30)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }

    public int? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueAt { get; set; }
    public DateTime? PaidAt { get; set; }

    [StringLength(200)]
    public string Description { get; set; } = string.Empty;

    public ICollection<PaymentTransaction> Payments { get; set; } = new List<PaymentTransaction>();
    public ICollection<RefundRequest> Refunds { get; set; } = new List<RefundRequest>();
}

public class PaymentTransaction
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    [StringLength(40)]
    public string Method { get; set; } = "SimulatedGateway";

    [StringLength(80)]
    public string? Reference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public class RefundRequest
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public RefundStatus Status { get; set; } = RefundStatus.Pending;

    [StringLength(300)]
    public string Reason { get; set; } = string.Empty;

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    public string? ProcessedByUserId { get; set; }
}

public class Announcement
{
    public int Id { get; set; }

    [Required, StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(2000)]
    public string Body { get; set; } = string.Empty;

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    public bool IsPublished { get; set; } = true;
}

public class AuditLog
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    [Required, StringLength(80)]
    public string Action { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Entity { get; set; }

    [StringLength(500)]
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SystemSetting
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string Key { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Value { get; set; } = string.Empty;
}
