namespace MIniProject.Services;

public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>When false, messages are only written to the fallback directory / logs.</summary>
    public bool Enabled { get; set; } = true;

    public string FromName { get; set; } = "Kota Permai Clubhouse";

    public string FromAddress { get; set; } = "noreply@kotapermai.local";

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>
    /// Relative to content root. Used when SMTP Host is empty or send fails in Development.
    /// </summary>
    public string FallbackDirectory { get; set; } = "App_Data/emails";

    /// <summary>Password-reset OTP lifetime in minutes.</summary>
    public int OtpExpiryMinutes { get; set; } = 15;

    /// <summary>Identity password-reset link lifetime in hours.</summary>
    public int ResetLinkExpiryHours { get; set; } = 1;
}

public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
