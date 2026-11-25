using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace MIniProject.Services;

/// <summary>
/// Sends via SMTP when <see cref="SmtpOptions.Host"/> is configured.
/// File fallback under <see cref="EmailOptions.FallbackDirectory"/> only when
/// <see cref="EmailOptions.AllowFileFallback"/> is true and Host is empty.
/// Failures are returned (not swallowed) so callers can notify the user.
/// </summary>
public class ConfigurableEmailSender : IAppEmailSender
{
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ConfigurableEmailSender> _logger;

    public ConfigurableEmailSender(
        IOptionsMonitor<EmailOptions> options,
        IWebHostEnvironment env,
        ILogger<ConfigurableEmailSender> logger)
    {
        _options = options;
        _env = env;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(
        string toEmail,
        string subject,
        string plainTextBody,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            return EmailSendResult.Failed("Recipient email is required.");
        }

        var options = _options.CurrentValue;

        if (!options.Enabled)
        {
            _logger.LogWarning("Email send blocked: Email:Enabled is false.");
            return EmailSendResult.Failed(
                "Email delivery is disabled. Set Email:Enabled to true and configure SMTP.");
        }

        var fromAddress = options.ResolveFromAddress();
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            _logger.LogError("Email send failed: Email:FromAddress is empty after resolving options.");
            return EmailSendResult.Failed(
                "Email:FromAddress is not configured. Set Email:FromAddress (or Email:FromEmail) via user secrets.");
        }

        if (string.Equals(fromAddress, EmailOptions.DefaultLocalFromAddress, StringComparison.OrdinalIgnoreCase) &&
            options.IsSmtpConfigured)
        {
            _logger.LogError(
                "Email send blocked: FromAddress is still '{From}' while SMTP Host is set. " +
                "Gmail and most providers reject .local senders. Set Email:FromAddress to your SMTP mailbox " +
                "(usually the same as Email:Smtp:UserName).",
                fromAddress);
            return EmailSendResult.Failed(
                "Email:FromAddress must be a real mailbox allowed by your SMTP provider " +
                "(for Gmail, use the same address as Email:Smtp:UserName).");
        }

        if (!string.Equals(fromAddress, options.FromAddress?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Using resolved FromAddress {Resolved} (configured FromAddress was {Configured}). " +
                "Prefer setting Email:FromAddress explicitly in user secrets.",
                fromAddress,
                options.FromAddress);
        }

        var smtpConfigured = options.IsSmtpConfigured;

        if (smtpConfigured)
        {
            try
            {
                await SendSmtpAsync(options, fromAddress, toEmail, subject, plainTextBody, ct);
                _logger.LogInformation(
                    "Email sent via SMTP host {Host}:{Port} from {From} to {To} subject {Subject}",
                    options.Smtp.Host,
                    options.Smtp.Port,
                    fromAddress,
                    toEmail,
                    subject);
                return EmailSendResult.SentViaSmtp();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "SMTP send failed via {Host}:{Port} from {From} to {To} subject {Subject}: {Message}",
                    options.Smtp.Host,
                    options.Smtp.Port,
                    fromAddress,
                    toEmail,
                    subject,
                    ex.Message);
                string? diagPath = null;
                try
                {
                    diagPath = await WriteFallbackAsync(options, fromAddress, toEmail, subject, plainTextBody, "smtp-error", ct);
                }
                catch (Exception writeEx)
                {
                    _logger.LogError(writeEx, "Also failed to write SMTP error diagnostic file.");
                }

                return EmailSendResult.Failed(
                    "We could not send the email via SMTP. Check Email:Smtp settings (Host, Port, FromAddress, credentials) and try again.",
                    diagPath);
            }
        }

        if (options.AllowFileFallback)
        {
            var path = await WriteFallbackAsync(options, fromAddress, toEmail, subject, plainTextBody, "no-smtp", ct);
            _logger.LogWarning(
                "Email:Smtp:Host is empty — wrote fallback file for {To} at {Path}. Configure SMTP for real delivery.",
                toEmail,
                path);
            return EmailSendResult.SentViaFile(path);
        }

        _logger.LogError(
            "Email send failed for {To}: SMTP Host empty and AllowFileFallback is false.",
            toEmail);
        return EmailSendResult.Failed(
            "Email delivery is not configured. Set Email:Smtp:Host (and credentials via user secrets or environment variables).");
    }

    private async Task SendSmtpAsync(
        EmailOptions options,
        string fromAddress,
        string toEmail,
        string subject,
        string plainTextBody,
        CancellationToken ct)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress, options.FromName),
            Subject = subject,
            Body = plainTextBody,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        message.To.Add(toEmail);

        using var client = new SmtpClient(options.Smtp.Host, options.Smtp.Port)
        {
            EnableSsl = options.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            // Avoid indefinite hangs when Host/Port are wrong.
            Timeout = 30_000
        };

        if (!string.IsNullOrWhiteSpace(options.Smtp.UserName))
        {
            client.Credentials = new NetworkCredential(options.Smtp.UserName, options.Smtp.Password);
        }

        ct.ThrowIfCancellationRequested();
        await client.SendMailAsync(message);
    }

    private async Task<string> WriteFallbackAsync(
        EmailOptions options,
        string fromAddress,
        string toEmail,
        string subject,
        string plainTextBody,
        string reason,
        CancellationToken ct)
    {
        var dir = ResolveFallbackDirectory(options);
        Directory.CreateDirectory(dir);

        var safeTo = string.Concat(toEmail.Select(c => char.IsLetterOrDigit(c) || c is '@' or '.' or '-' or '_' ? c : '_'));
        var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{safeTo}-{reason}.txt";
        var path = Path.Combine(dir, fileName);

        var content = new StringBuilder()
            .AppendLine($"From: {options.FromName} <{fromAddress}>")
            .AppendLine($"To: {toEmail}")
            .AppendLine($"Subject: {subject}")
            .AppendLine($"Utc: {DateTime.UtcNow:O}")
            .AppendLine($"Reason: {reason}")
            .AppendLine()
            .Append(plainTextBody)
            .ToString();

        await File.WriteAllTextAsync(path, content, Encoding.UTF8, ct);
        return path;
    }

    private string ResolveFallbackDirectory(EmailOptions options)
    {
        var configured = string.IsNullOrWhiteSpace(options.FallbackDirectory)
            ? "App_Data/emails"
            : options.FallbackDirectory;

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_env.ContentRootPath, configured);
    }
}
