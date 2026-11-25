namespace MIniProject.Services;

public enum EmailDeliveryMode
{
    /// <summary>Message accepted by the configured SMTP server.</summary>
    Smtp,

    /// <summary>SMTP not configured; message written under FallbackDirectory (dev/local only).</summary>
    FileFallback
}

public sealed class EmailSendResult
{
    public bool Success { get; init; }

    public EmailDeliveryMode? Mode { get; init; }

    /// <summary>User-safe explanation when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Diagnostic path when a fallback file was written (success or failure capture).</summary>
    public string? FallbackPath { get; init; }

    public static EmailSendResult SentViaSmtp() => new()
    {
        Success = true,
        Mode = EmailDeliveryMode.Smtp
    };

    public static EmailSendResult SentViaFile(string path) => new()
    {
        Success = true,
        Mode = EmailDeliveryMode.FileFallback,
        FallbackPath = path
    };

    public static EmailSendResult Failed(string message, string? fallbackPath = null) => new()
    {
        Success = false,
        ErrorMessage = message,
        FallbackPath = fallbackPath
    };
}
