namespace MIniProject.Models;

/// <summary>
/// Canonical facility/equipment registry categories used by the admin Category select.
/// </summary>
public static class FacilityCategories
{
    public static readonly IReadOnlyList<string> Allowed =
    [
        "Banquet",
        "Golf",
        "Leisure",
        "Meeting",
        "Sports"
    ];

    public static bool IsAllowed(string? category) =>
        !string.IsNullOrWhiteSpace(category) &&
        Allowed.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the dropdown list: allowed categories plus any already stored values
    /// (so legacy/custom rows remain selectable when editing).
    /// </summary>
    public static List<string> MergeWithExisting(IEnumerable<string?> existing, string? current = null)
    {
        return Allowed
            .Concat(existing.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()))
            .Concat(string.IsNullOrWhiteSpace(current) ? [] : [current.Trim()])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
