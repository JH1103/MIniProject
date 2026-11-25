namespace MIniProject.Services;

public interface IAppEmailSender
{
    /// <summary>
    /// Attempts delivery. Callers must check <see cref="EmailSendResult.Success"/> and surface failures.
    /// </summary>
    Task<EmailSendResult> SendAsync(string toEmail, string subject, string plainTextBody, CancellationToken ct = default);
}
