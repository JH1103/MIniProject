using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MIniProject.Models;
using MIniProject.Services;
using MIniProject.ViewModels;

namespace MIniProject.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly PasswordResetService _passwordResetService;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        PasswordResetService passwordResetService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _passwordResetService = passwordResetService;
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existing = await _userManager.FindByEmailAsync(model.Email);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists. Please sign in instead.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            FullName = model.FullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim(),
            IdentificationNumber = string.IsNullOrWhiteSpace(model.IdentificationNumber) ? null : model.IdentificationNumber.Trim(),
            PdpaConsent = model.PdpaConsent,
            PdpaConsentAt = DateTime.UtcNow,
            MembershipStatus = MembershipStatus.Expired,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, AppRoles.Member);
        await _signInManager.SignInAsync(user, isPersistent: false);
        TempData["Success"] = "Welcome! Choose a membership plan to start booking facilities.";
        return RedirectToAction("Plans", "Membership");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is not null && await _userManager.IsInRoleAsync(user, AppRoles.Administrator))
        {
            return RedirectToAction("Index", "Admin");
        }

        if (user is not null && await _userManager.IsInRoleAsync(user, AppRoles.Staff))
        {
            return RedirectToAction("Index", "Staff");
        }

        return RedirectToAction("Dashboard", "Member");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var resetUrl = Url.Action(
            nameof(ResetPassword),
            "Account",
            values: null,
            protocol: Request.Scheme)!;

        var (ok, error, mode) = await _passwordResetService.RequestResetAsync(model.Email, resetUrl);
        if (!ok)
        {
            ModelState.AddModelError(nameof(model.Email), error ?? "Unable to process password reset.");
            TempData["Error"] = error ?? "Unable to process password reset.";
            return View(model);
        }

        if (mode == EmailDeliveryMode.FileFallback)
        {
            TempData["Warning"] =
                "SMTP is not configured, so the reset message was saved locally under App_Data/emails (Development fallback). Configure Email:Smtp:Host for real inbox delivery.";
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    [HttpGet]
    public IActionResult ResetPassword(string? email = null, string? token = null)
    {
        return View(new ResetPasswordViewModel
        {
            Email = email ?? string.Empty,
            Token = token
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!model.HasLinkToken && string.IsNullOrWhiteSpace(model.Otp))
        {
            ModelState.AddModelError(nameof(model.Otp), "Enter the 6-digit OTP from your email, or use the reset link.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        (bool Ok, string? Error) result;
        if (model.HasLinkToken)
        {
            result = await _passwordResetService.ResetWithTokenAsync(model.Email, model.Token!, model.Password);
        }
        else
        {
            result = await _passwordResetService.ResetWithOtpAsync(model.Email, model.Otp!, model.Password);
        }

        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Unable to reset password.");
            return View(model);
        }

        TempData["Success"] = "Your password has been updated. You can sign in with your new password.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
