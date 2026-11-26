using System.ComponentModel.DataAnnotations;

namespace MIniProject.Models;

/// <summary>
/// Club-wide equipment catalog. Facility inventory links pick from these items.
/// </summary>
public class EquipmentCatalogEntry
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    [Display(Name = "Equipment name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}
