namespace MIniProject.Services;

/// <summary>
/// Equipment rental fees are charged at full list price.
/// Member facility discounts never apply to equipment.
/// </summary>
public static class EquipmentPricing
{
    public const decimal FeePerUnitPerDay = 15m;

    public static int BillableDays(DateTime startUtc, DateTime endUtc)
    {
        if (endUtc <= startUtc)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Ceiling((endUtc - startUtc).TotalDays));
    }

    public static decimal CalculateFee(int quantity, DateTime startUtc, DateTime endUtc)
        => Math.Round(FeePerUnitPerDay * Math.Max(1, quantity) * BillableDays(startUtc, endUtc), 2);
}
