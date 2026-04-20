namespace MyApp.Services;

/// <summary>
/// Service interface for processing payments through different payment gateways.
/// Multiple implementations allow testing different payment providers.
/// </summary>
public interface IPaymentProcessor
{
    Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request);
    Task<PaymentResult> RefundPaymentAsync(string transactionId, decimal amount);
    Task<bool> ValidatePaymentMethodAsync(PaymentMethod paymentMethod);
    Task<PaymentStatus> GetPaymentStatusAsync(string transactionId);
    string GetProviderName();
    decimal GetProcessingFee(decimal amount);
}

public class PaymentRequest
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentMethod PaymentMethod { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class PaymentMethod
{
    public PaymentType Type { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Last4Digits { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
}

public class PaymentResult
{
    public bool Success { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public decimal ProcessedAmount { get; set; }
    public decimal ProcessingFee { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime ProcessedAt { get; set; }
    public Dictionary<string, string> ProviderData { get; set; } = new();
}

public enum PaymentType
{
    CreditCard,
    DebitCard,
    BankTransfer,
    DigitalWallet,
    Cryptocurrency
}

public enum PaymentStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Cancelled,
    Refunded
}
