using System.ComponentModel.DataAnnotations;
using MIniProject.Models;

namespace MIniProject.ViewModels;

public class PaymentMethodViewModel
{
    public Invoice Invoice { get; set; } = null!;
}

public class CardPaymentViewModel
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Cardholder name is required.")]
    [Display(Name = "Name on card")]
    [StringLength(80, MinimumLength = 2)]
    public string CardholderName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Card number is required.")]
    [Display(Name = "Card number")]
    [RegularExpression(@"^[\d\s]{13,23}$", ErrorMessage = "Enter a valid card number.")]
    public string CardNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Expiry month is required.")]
    [Display(Name = "Expiry month")]
    [Range(1, 12, ErrorMessage = "Month must be between 1 and 12.")]
    public int ExpiryMonth { get; set; }

    [Required(ErrorMessage = "Expiry year is required.")]
    [Display(Name = "Expiry year")]
    public int ExpiryYear { get; set; }

    [Required(ErrorMessage = "CVV is required.")]
    [Display(Name = "CVV")]
    [RegularExpression(@"^\d{3,4}$", ErrorMessage = "CVV must be 3 or 4 digits.")]
    [DataType(DataType.Password)]
    public string Cvv { get; set; } = string.Empty;
}

public class EWalletPaymentViewModel
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    public string QrBase64 { get; set; } = string.Empty;
}
