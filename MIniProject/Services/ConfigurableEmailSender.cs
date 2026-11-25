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
    private readonly EmailOptions _options;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ConfigurableEmailSender> _logger;

    public ConfigurableEmailSender(
        IOptions<EmailOptions> options,
        IWebHostEnvironment env,
        ILogger<ConfigurableEmailSender> logger)
    {
        _options = options.Value;
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

        if (!_options.Enabled)
        {
            _logger.LogWarning("Email send blocked: Email:Enabled is false.");
            return EmailSendResult.Failed(
                "Email delivery is disabled. Set Email:Enabled to true and configure SMTP.");
        }

        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            return EmailSendResult.Failed("Email:FromAddress is not configured.");
        }

        var smtpConfigured = !string.IsNullOrWhiteSpace(_options.Smtp.Host);

        if (smtpConfigured)
        {
            try
            {
                await SendSmtpAsync(toEmail, subject, plainTextBody, ct);
                _logger.LogInformation("Email sent via SMTP to {To} subject {Subject}", toEmail, subject);
                return EmailSendResult.SentViaSmtp();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMTP send failed for {To} subject {Subject}", toEmail, subject);
                string? diagPath = null;
                try
                {
                    diagPath = await WriteFallbackAsync(toEmail, subject, plainTextBody, "smtp-error", ct);
                }
                catch (Exception writeEx)
                {
                    _logger.LogError(writeEx, "Also failed to write SMTP error diagnostic file.");
                }

                return EmailSendResult.Failed(
                    "We could not send the email via SMTP. Check Email:Smtp settings (Host, Port, credentials) and try again.",
                    diagPath);
            }
        }

        if (_options.AllowFileFallback)
        {
            var path = await WriteFallbackAsync(toEmail, subject, plainTextBody, "no-smtp", ct);
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

    private async Task SendSmtpAsync(string toEmail, string subject, string plainTextBody, CancellationToken ct)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = plainTextBody,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        message.To.Add(toEmail);

        using var client = new SmtpClient(_options.Smtp.Host, _options.Smtp.Port)
        {
            EnableSsl = _options.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            // Avoid indefinite hangs when Host/Port are wrong.
            Timeout = 30_000
        };

        if (!string.IsNullOrWhiteSpace(_options.Smtp.UserName))
        {
            client.Credentials = new NetworkCredential(_options.Smtp.UserName, _options.Smtp.Password);
        }

        ct.ThrowIfCancellationRequested();
        await client.SendMailAsync(message);
    }

    private async Task<string> WriteFallbackAsync(
        string toEmail,
        string subject,
        string plainTextBody,
        string reason,
        CancellationToken ct)
    {
        var dir = ResolveFallbackDirectory();
        Directory.CreateDirectory(dir);

        var safeTo = string.Concat(toEmail.Select(c => char.IsLetterOrDigit(c) || c is '@' or '.' or '-' or '_' ? c : '_'));
        var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{safeTo}-{reason}.txt";
        var path = Path.Combine(dir, fileName);

        var content = new StringBuilder()
            .AppendLine($"From: {_options.FromName} <{_options.FromAddress}>")
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

    private string ResolveFallbackDirectory()
    {
        var configured = string.IsNullOrWhiteSpace(_options.FallbackDirectory)
            ? "App_Data/emails"
            : _options.FallbackDirectory;

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_env.ContentRootPath, configured);
    }
}
