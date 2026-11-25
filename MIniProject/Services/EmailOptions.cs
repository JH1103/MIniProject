namespace MIniProject.Services;

public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Master switch. When false, sends fail with a clear error (no silent success).
    /// </summary>
    public bool Enabled { get; set; } = true;

    public string FromName { get; set; } = "Kota Permai Clubhouse";

    public string FromAddress { get; set; } = "noreply@kotapermai.local";

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>
    /// Relative to content root. Used when SMTP Host is empty and <see cref="AllowFileFallback"/> is true,
    /// or to capture a diagnostic copy after an SMTP failure.
    /// </summary>
    public string FallbackDirectory { get; set; } = "App_Data/emails";

    /// <summary>
    /// When true and SMTP Host is empty, write message files locally instead of failing.
    /// Keep true only for Development; set false (or leave SMTP configured) for real delivery.
    /// </summary>
    public bool AllowFileFallback { get; set; } = false;

    /// <summary>Password-reset OTP lifetime in minutes.</summary>
    public int OtpExpiryMinutes { get; set; } = 15;

    /// <summary>Identity password-reset link lifetime in hours.</summary>
    public int ResetLinkExpiryHours { get; set; } = 1;
}

public class SmtpOptions
{
    /// <summary>Required for real delivery (e.g. smtp.gmail.com, smtp.sendgrid.net).</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    /// <summary>Optional SMTP auth username — prefer user secrets / env vars.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Optional SMTP auth password — prefer user secrets / env vars. Never commit real values.</summary>
    public string Password { get; set; } = string.Empty;
}
