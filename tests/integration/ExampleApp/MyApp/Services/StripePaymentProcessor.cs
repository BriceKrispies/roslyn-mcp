using Microsoft.Extensions.Logging;

namespace MyApp.Services;

/// <summary>
/// Stripe payment processor implementation of IPaymentProcessor.
/// This implementation integrates with Stripe's payment processing API.
/// </summary>
public class StripePaymentProcessor : IPaymentProcessor
{
    private readonly ILogger<StripePaymentProcessor> _logger;
    private readonly string _apiKey;
    private readonly decimal _processingFeeRate;

    public StripePaymentProcessor(ILogger<StripePaymentProcessor> logger)
    {
        _logger = logger;
        _apiKey = "sk_test_fake_stripe_key_12345"; // In real app, secure configuration
        _processingFeeRate = 0.029m; // 2.9% + $0.30
    }

    public async Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request)
    {
        _logger.LogInformation("Processing payment of {Amount} {Currency} via Stripe", 
            request.Amount, request.Currency);

        var result = new PaymentResult
        {
            ProcessedAt = DateTime.UtcNow,
            ProcessedAmount = request.Amount
        };

        try
        {
            // Simulate Stripe API call delay
            await Task.Delay(800);

            // Calculate processing fee
            result.ProcessingFee = GetProcessingFee(request.Amount);

            // Simulate payment validation
            if (request.Amount <= 0)
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Invalid payment amount";
                return result;
            }

            if (request.PaymentMethod.Token.Contains("fail"))
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Payment method declined by issuer";
                return result;
            }

            // Simulate successful payment
            result.Success = true;
            result.Status = PaymentStatus.Completed;
            result.TransactionId = $"pi_stripe_{DateTime.UtcNow.Ticks}";
            result.ProviderData.Add("stripe_payment_intent_id", result.TransactionId);
            result.ProviderData.Add("stripe_receipt_url", $"https://dashboard.stripe.com/payments/{result.TransactionId}");

            _logger.LogInformation("Stripe payment successful: {TransactionId}, Amount: {Amount}", 
                result.TransactionId, request.Amount);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe payment processing error for amount {Amount}", request.Amount);
            result.Success = false;
            result.Status = PaymentStatus.Failed;
            result.ErrorMessage = "Payment processing error occurred";
            return result;
        }
    }

    public async Task<PaymentResult> RefundPaymentAsync(string transactionId, decimal amount)
    {
        _logger.LogInformation("Processing Stripe refund for transaction {TransactionId}, amount {Amount}", 
            transactionId, amount);

        var result = new PaymentResult
        {
            ProcessedAt = DateTime.UtcNow,
            ProcessedAmount = amount,
            TransactionId = $"re_stripe_{DateTime.UtcNow.Ticks}"
        };

        try
        {
            // Simulate Stripe refund API call
            await Task.Delay(600);

            if (!transactionId.StartsWith("pi_stripe_"))
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Invalid Stripe transaction ID";
                return result;
            }

            result.Success = true;
            result.Status = PaymentStatus.Refunded;
            result.ProviderData.Add("stripe_refund_id", result.TransactionId);
            result.ProviderData.Add("original_payment_intent", transactionId);

            _logger.LogInformation("Stripe refund successful: {RefundId} for original {TransactionId}", 
                result.TransactionId, transactionId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe refund error for transaction {TransactionId}", transactionId);
            result.Success = false;
            result.Status = PaymentStatus.Failed;
            result.ErrorMessage = "Refund processing error occurred";
            return result;
        }
    }

    public async Task<bool> ValidatePaymentMethodAsync(PaymentMethod paymentMethod)
    {
        _logger.LogInformation("Validating Stripe payment method: {Type}", paymentMethod.Type);

        // Simulate Stripe payment method validation
        await Task.Delay(200);

        return paymentMethod.Type switch
        {
            PaymentType.CreditCard => ValidateCreditCard(paymentMethod),
            PaymentType.DebitCard => ValidateDebitCard(paymentMethod),
            PaymentType.DigitalWallet => ValidateDigitalWallet(paymentMethod),
            _ => false
        };
    }

    public async Task<PaymentStatus> GetPaymentStatusAsync(string transactionId)
    {
        _logger.LogInformation("Checking Stripe payment status for transaction: {TransactionId}", transactionId);

        // Simulate Stripe API status check
        await Task.Delay(150);

        if (!transactionId.StartsWith("pi_stripe_"))
        {
            return PaymentStatus.Failed;
        }

        // Simulate various payment statuses based on transaction ID patterns
        if (transactionId.Contains("pending"))
            return PaymentStatus.Pending;
        if (transactionId.Contains("processing"))
            return PaymentStatus.Processing;
        if (transactionId.Contains("failed"))
            return PaymentStatus.Failed;

        return PaymentStatus.Completed; // Default to completed for valid Stripe transactions
    }

    public string GetProviderName()
    {
        return "Stripe";
    }

    public decimal GetProcessingFee(decimal amount)
    {
        // Stripe's standard pricing: 2.9% + $0.30
        return Math.Round(amount * _processingFeeRate + 0.30m, 2);
    }

    private bool ValidateCreditCard(PaymentMethod paymentMethod)
    {
        return !string.IsNullOrWhiteSpace(paymentMethod.Token) &&
               !string.IsNullOrWhiteSpace(paymentMethod.Last4Digits) &&
               paymentMethod.Last4Digits.Length == 4 &&
               !string.IsNullOrWhiteSpace(paymentMethod.ExpiryMonth) &&
               !string.IsNullOrWhiteSpace(paymentMethod.ExpiryYear);
    }

    private bool ValidateDebitCard(PaymentMethod paymentMethod)
    {
        // Same validation as credit card for Stripe
        return ValidateCreditCard(paymentMethod);
    }

    private bool ValidateDigitalWallet(PaymentMethod paymentMethod)
    {
        return !string.IsNullOrWhiteSpace(paymentMethod.Token) &&
               (paymentMethod.Token.StartsWith("google_pay_") || 
                paymentMethod.Token.StartsWith("apple_pay_"));
    }
}
