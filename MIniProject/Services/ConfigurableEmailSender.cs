using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace MIniProject.Services;

/// <summary>
/// Sends via SMTP when <see cref="SmtpOptions.Host"/> is configured; otherwise writes
/// message files under <see cref="EmailOptions.FallbackDirectory"/> (safe local/dev fallback).
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

    public async Task SendAsync(string toEmail, string subject, string plainTextBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            throw new ArgumentException("Recipient email is required.", nameof(toEmail));
        }

        var smtpConfigured = !string.IsNullOrWhiteSpace(_options.Smtp.Host);

        if (_options.Enabled && smtpConfigured)
        {
            try
            {
                await SendSmtpAsync(toEmail, subject, plainTextBody, ct);
                _logger.LogInformation("Email sent via SMTP to {To} subject {Subject}", toEmail, subject);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMTP send failed for {To}; writing fallback file.", toEmail);
                await WriteFallbackAsync(toEmail, subject, plainTextBody, "smtp-error", ct);
                return;
            }
        }

        await WriteFallbackAsync(toEmail, subject, plainTextBody, smtpConfigured ? "disabled" : "no-smtp", ct);
        _logger.LogInformation(
            "Email fallback ({Reason}) for {To} subject {Subject}. Check {Dir}.",
            smtpConfigured ? "disabled" : "SMTP Host empty",
            toEmail,
            subject,
            ResolveFallbackDirectory());
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
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(_options.Smtp.UserName))
        {
            client.Credentials = new NetworkCredential(_options.Smtp.UserName, _options.Smtp.Password);
        }

        // SmtpClient.SendMailAsync ignores CancellationToken on older APIs; wrap for cooperative cancel.
        ct.ThrowIfCancellationRequested();
        await client.SendMailAsync(message);
    }

    private async Task WriteFallbackAsync(
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
