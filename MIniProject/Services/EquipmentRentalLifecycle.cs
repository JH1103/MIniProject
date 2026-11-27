using MIniProject.Models;

namespace MIniProject.Services;

/// <summary>
/// Pickup / overdue rules for equipment rentals:
/// Active (awaiting pickup) → Inactive if not picked up within 15 minutes of start;
/// staff mark taken → Rented (only when equipment rental is paid); due passed while out → NotReturned;
/// Return is only allowed while Rented.
/// </summary>
public static class EquipmentRentalLifecycle
{
    public static readonly TimeSpan PickupGrace = TimeSpan.FromMinutes(15);

    public static string DisplayLabel(EquipmentRentalStatus status) => status switch
    {
        EquipmentRentalStatus.Active => "Awaiting pickup",
        EquipmentRentalStatus.Rented => "Rented",
        EquipmentRentalStatus.Returned => "Returned",
        EquipmentRentalStatus.Cancelled => "Inactive",
        EquipmentRentalStatus.Inactive => "Inactive",
        EquipmentRentalStatus.NotReturned => "Not returned",
        EquipmentRentalStatus.Invalid => "Invalid",
        _ => status.ToString()
    };

    public static bool IsOpenPossession(EquipmentRentalStatus status) =>
        status is EquipmentRentalStatus.Rented or EquipmentRentalStatus.NotReturned;

    public static bool CanMarkPickedUp(EquipmentRentalStatus status) =>
        status == EquipmentRentalStatus.Active;

    /// <summary>
    /// Members and staff may cancel only before pickup (Awaiting pickup / Active).
    /// </summary>
    public static bool CanCancel(EquipmentRentalStatus status) =>
        status == EquipmentRentalStatus.Active;

    /// <summary>
    /// Return requires prior pickup: Rented, or NotReturned (was rented, past due).
    /// Awaiting pickup (Active) / Inactive cannot be marked returned.
    /// </summary>
    public static bool CanMarkReturned(EquipmentRentalStatus status) =>
        status is EquipmentRentalStatus.Rented or EquipmentRentalStatus.NotReturned;

    /// <summary>
    /// Applies time-based transitions in memory. Returns true when status (or stock) changed.
    /// Caller must SaveChanges.
    /// </summary>
    public static bool ApplyTimeBasedTransitions(EquipmentRental rental, DateTime utcNow)
    {
        var changed = false;

        if (rental.Status == EquipmentRentalStatus.Active &&
            utcNow > rental.StartUtc.Add(PickupGrace) &&
            rental.PickedUpAt is null)
        {
            rental.Status = EquipmentRentalStatus.Inactive;
            RestoreStock(rental);
            changed = true;
        }
        else if (rental.Status == EquipmentRentalStatus.Rented &&
                 utcNow > rental.DueUtc &&
                 rental.ReturnedAt is null)
        {
            rental.Status = EquipmentRentalStatus.NotReturned;
            changed = true;
        }

        return changed;
    }

    public static void MarkPickedUp(EquipmentRental rental, DateTime utcNow)
    {
        if (!CanMarkPickedUp(rental.Status))
        {
            throw new InvalidOperationException("Only awaiting-pickup rentals can be marked as taken.");
        }

        rental.Status = EquipmentRentalStatus.Rented;
        rental.PickedUpAt = utcNow;
    }

    public static void MarkReturned(EquipmentRental rental, DateTime utcNow)
    {
        if (!CanMarkReturned(rental.Status))
        {
            throw new InvalidOperationException(
                "Only equipment that was picked up (Rented / Not returned) can be marked as returned.");
        }

        rental.Status = EquipmentRentalStatus.Returned;
        rental.ReturnedAt = utcNow;
        RestoreStock(rental);
    }

    public static void RestoreStock(EquipmentRental rental)
    {
        if (rental.InventoryItem is not null)
        {
            rental.InventoryItem.Quantity += rental.Quantity;
        }
    }
}
