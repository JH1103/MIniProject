using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MIniProject.Models;

public class Facility
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(80)]
    public string Category { get; set; } = string.Empty;

    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    [StringLength(300)]
    public string? ImageUrl { get; set; }

    public int Capacity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal HourlyRate { get; set; }

    public TimeSpan OpenTime { get; set; } = new(8, 0, 0);
    public TimeSpan CloseTime { get; set; } = new(22, 0, 0);

    public int SlotDurationMinutes { get; set; } = 60;

    public BookingMode BookingMode { get; set; } = BookingMode.InstantConfirmation;

    public FacilityStatus Status { get; set; } = FacilityStatus.Available;

    public bool IsActive { get; set; } = true;

    public int PaymentHoldMinutes { get; set; } = 15;

    public ICollection<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();
    public ICollection<FacilityBlockOut> BlockOuts { get; set; } = new List<FacilityBlockOut>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}

public class InventoryItem
{
    public int Id { get; set; }

    public int FacilityId { get; set; }
    public Facility? Facility { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public InventoryCondition Condition { get; set; } = InventoryCondition.Good;

    [StringLength(500)]
    public string? Notes { get; set; }
}

public class FacilityBlockOut
{
    public int Id { get; set; }

    public int FacilityId { get; set; }
    public Facility? Facility { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    [StringLength(200)]
    public string Reason { get; set; } = "Maintenance";
}
