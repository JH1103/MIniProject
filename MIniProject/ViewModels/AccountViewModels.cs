using System.ComponentModel.DataAnnotations;

namespace MIniProject.ViewModels;

public class RegisterViewModel
{
    [Required, Display(Name = "Full name")]
    [StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Phone number")]
    [RegularExpression(@"^$|^[\d\s\-+()]{8,20}$", ErrorMessage = "Enter a valid phone number.")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "NRIC / Passport number")]
    [StringLength(50)]
    public string? IdentificationNumber { get; set; }

    [Required, DataType(DataType.Password)]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$",
        ErrorMessage = "Password must include uppercase, lowercase and a number.")]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Password and confirmation do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [MustBeTrue(ErrorMessage = "Please tick the PDPA consent box to continue.")]
    [Display(Name = "I consent to the processing of my personal data under PDPA 2010")]
    public bool PdpaConsent { get; set; }
}

/// <summary>Ensures a checkbox was ticked (true).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MustBeTrueAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is true;
}

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}
