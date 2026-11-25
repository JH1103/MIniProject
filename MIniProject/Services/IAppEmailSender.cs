namespace MIniProject.Services;

public interface IAppEmailSender
{
    Task SendAsync(string toEmail, string subject, string plainTextBody, CancellationToken ct = default);
}
