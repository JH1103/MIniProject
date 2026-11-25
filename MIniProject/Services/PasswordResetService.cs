using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using MIniProject.Models;

namespace MIniProject.Services;

public class PasswordResetService
{
    public const string TokenProviderLoginProvider = "PasswordReset";
    public const string OtpHashTokenName = "OtpHash";
    public const string OtpExpiresTokenName = "OtpExpires";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAppEmailSender _emailSender;
    private readonly EmailOptions _options;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        UserManager<ApplicationUser> userManager,
        IAppEmailSender emailSender,
        IOptions<EmailOptions> options,
        ILogger<PasswordResetService> logger)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Always succeeds from the caller's perspective (no account enumeration).
    /// Sends OTP + reset link when the email matches a member account.
    /// </summary>
    public async Task RequestResetAsync(string email, string resetUrlBase, CancellationToken ct = default)
    {
        var normalized = email.Trim();
        var user = await _userManager.FindByEmailAsync(normalized);
        if (user is null)
        {
            _logger.LogInformation("Password reset requested for unknown email.");
            return;
        }

        var otp = GenerateOtp();
        var expires = DateTime.UtcNow.AddMinutes(Math.Max(1, _options.OtpExpiryMinutes));
        var otpHash = HashOtp(user.Id, otp);

        await _userManager.SetAuthenticationTokenAsync(user, TokenProviderLoginProvider, OtpHashTokenName, otpHash);
        await _userManager.SetAuthenticationTokenAsync(
            user,
            TokenProviderLoginProvider,
            OtpExpiresTokenName,
            expires.ToString("O"));

        var identityToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(identityToken));
        var link = QueryHelpers.AddQueryString(resetUrlBase, new Dictionary<string, string?>
        {
            ["email"] = user.Email,
            ["token"] = encodedToken
        });

        var expiryMinutes = Math.Max(1, _options.OtpExpiryMinutes);
        var linkHours = Math.Max(1, _options.ResetLinkExpiryHours);
        var body = $"""
            Hello {user.FullName},

            We received a request to reset your Kota Permai Clubhouse password.

            One-time code (OTP): {otp}
            This code expires in {expiryMinutes} minute(s).

            Or open this link to choose a new password (expires in {linkHours} hour(s)):
            {link}

            If you did not request this, you can ignore this email.

            — Kota Permai Golf & Country Club
            """;

        await _emailSender.SendAsync(
            user.Email!,
            "Reset your Kota Permai password",
            body,
            ct);
    }

    public async Task<(bool Ok, string? Error)> ResetWithTokenAsync(
        string email,
        string encodedToken,
        string newPassword,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            return (false, "Invalid or expired reset link. Request a new one.");
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
        }
        catch (Exception)
        {
            return (false, "Invalid or expired reset link. Request a new one.");
        }

        var result = await _userManager.ResetPasswordAsync(user, decoded, newPassword);
        if (!result.Succeeded)
        {
            var message = result.Errors.FirstOrDefault()?.Description
                ?? "Unable to reset password. The link may have expired.";
            return (false, message);
        }

        await ClearOtpAsync(user);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> ResetWithOtpAsync(
        string email,
        string otp,
        string newPassword,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            return (false, "Invalid email or OTP. Check your entry or request a new code.");
        }

        var storedHash = await _userManager.GetAuthenticationTokenAsync(user, TokenProviderLoginProvider, OtpHashTokenName);
        var expiresRaw = await _userManager.GetAuthenticationTokenAsync(user, TokenProviderLoginProvider, OtpExpiresTokenName);

        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(expiresRaw))
        {
            return (false, "No active reset code for this account. Request a new one.");
        }

        if (!DateTime.TryParse(expiresRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expires)
            || expires < DateTime.UtcNow)
        {
            await ClearOtpAsync(user);
            return (false, "This OTP has expired. Request a new password reset.");
        }

        var provided = (otp ?? string.Empty).Trim().Replace(" ", string.Empty);
        if (provided.Length != 6 || !provided.All(char.IsDigit))
        {
            return (false, "Enter the 6-digit OTP from your email.");
        }

        var providedHash = HashOtp(user.Id, provided);
        var storedBytes = Encoding.UTF8.GetBytes(storedHash);
        var providedBytes = Encoding.UTF8.GetBytes(providedHash);
        if (storedBytes.Length != providedBytes.Length
            || !CryptographicOperations.FixedTimeEquals(storedBytes, providedBytes))
        {
            return (false, "Invalid email or OTP. Check your entry or request a new code.");
        }

        // OTP validated — use Identity reset token generated now (single-use OTP gate).
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            var message = result.Errors.FirstOrDefault()?.Description
                ?? "Unable to reset password.";
            return (false, message);
        }

        await ClearOtpAsync(user);
        return (true, null);
    }

    private async Task ClearOtpAsync(ApplicationUser user)
    {
        await _userManager.RemoveAuthenticationTokenAsync(user, TokenProviderLoginProvider, OtpHashTokenName);
        await _userManager.RemoveAuthenticationTokenAsync(user, TokenProviderLoginProvider, OtpExpiresTokenName);
    }

    private static string GenerateOtp()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }

    private static string HashOtp(string userId, string otp)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"kp-reset|{userId}|{otp}"));
        return Convert.ToHexString(bytes);
    }
}
