using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MIniProject.Models;

public class EquipmentRental
{
    public int Id { get; set; }

    [Required]
    public string MemberId { get; set; } = string.Empty;
    public ApplicationUser? Member { get; set; }

    public int InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; } = 1;

    public DateTime StartUtc { get; set; }
    public DateTime DueUtc { get; set; }

    /// <summary>When staff confirmed the member took / checked out the equipment.</summary>
    public DateTime? PickedUpAt { get; set; }

    /// <summary>Staff/admin who confirmed pickup (mark taken).</summary>
    public string? PickedUpByUserId { get; set; }

    /// <summary>Denormalized staff/admin display name at pickup time.</summary>
    [StringLength(100)]
    public string? PickedUpByName { get; set; }

    public DateTime? ReturnedAt { get; set; }

    public EquipmentRentalStatus Status { get; set; } = EquipmentRentalStatus.Active;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Fee { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
