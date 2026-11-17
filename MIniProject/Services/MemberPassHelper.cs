namespace MIniProject.Services;

public static class MemberPassHelper
{
    public const string Prefix = "MEM-";

    public static string BuildPassCode(string userId) =>
        $"{Prefix}{userId.Replace("-", string.Empty).ToUpperInvariant()}";

    public static bool TryParseUserId(string? passCode, out string userId)
    {
        userId = string.Empty;
        if (string.IsNullOrWhiteSpace(passCode))
        {
            return false;
        }

        var raw = passCode.Trim().ToUpperInvariant();
        if (!raw.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var hex = raw[Prefix.Length..];
        if (hex.Length != 32)
        {
            return false;
        }

        // Reconstruct GUID: 8-4-4-4-12
        userId = $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}".ToLowerInvariant();
        return Guid.TryParse(userId, out _);
    }
}
