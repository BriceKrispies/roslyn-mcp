using Microsoft.Extensions.Logging;

namespace MyApp.Services;

/// <summary>
/// PayPal payment processor implementation of IPaymentProcessor.
/// This implementation integrates with PayPal's payment processing API.
/// </summary>
public class PayPalPaymentProcessor : IPaymentProcessor
{
    private readonly ILogger<PayPalPaymentProcessor> _logger;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly decimal _processingFeeRate;

    public PayPalPaymentProcessor(ILogger<PayPalPaymentProcessor> logger)
    {
        _logger = logger;
        _clientId = "fake_paypal_client_id_67890";
        _clientSecret = "fake_paypal_client_secret_54321";
        _processingFeeRate = 0.0349m; // 3.49% + $0.49 for standard transactions
    }

    public async Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request)
    {
        _logger.LogInformation("Processing payment of {Amount} {Currency} via PayPal", 
            request.Amount, request.Currency);

        var result = new PaymentResult
        {
            ProcessedAt = DateTime.UtcNow,
            ProcessedAmount = request.Amount
        };

        try
        {
            // Simulate PayPal OAuth token acquisition
            await Task.Delay(300);
            
            // Simulate PayPal payment creation and execution
            await Task.Delay(1200); // PayPal tends to be slower than Stripe

            result.ProcessingFee = GetProcessingFee(request.Amount);

            // Validate payment request
            if (request.Amount <= 0 || request.Amount > 10000) // PayPal limits
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Payment amount outside PayPal limits";
                return result;
            }

            if (request.PaymentMethod.Token.Contains("insufficient"))
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Insufficient funds in PayPal account";
                return result;
            }

            if (request.PaymentMethod.Token.Contains("restricted"))
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "PayPal account restricted";
                return result;
            }

            // Simulate successful payment
            result.Success = true;
            result.Status = PaymentStatus.Completed;
            result.TransactionId = $"PAY-{Guid.NewGuid():N}".ToUpper();
            result.ProviderData.Add("paypal_payment_id", result.TransactionId);
            result.ProviderData.Add("paypal_payer_id", $"PAYER{Random.Shared.Next(1000000, 9999999)}");
            result.ProviderData.Add("paypal_intent", "sale");

            _logger.LogInformation("PayPal payment successful: {TransactionId}, Amount: {Amount}", 
                result.TransactionId, request.Amount);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal payment processing error for amount {Amount}", request.Amount);
            result.Success = false;
            result.Status = PaymentStatus.Failed;
            result.ErrorMessage = "PayPal processing error occurred";
            return result;
        }
    }

    public async Task<PaymentResult> RefundPaymentAsync(string transactionId, decimal amount)
    {
        _logger.LogInformation("Processing PayPal refund for transaction {TransactionId}, amount {Amount}", 
            transactionId, amount);

        var result = new PaymentResult
        {
            ProcessedAt = DateTime.UtcNow,
            ProcessedAmount = amount,
            TransactionId = $"REFUND-{Guid.NewGuid():N}".ToUpper()
        };

        try
        {
            // Simulate PayPal refund API calls
            await Task.Delay(800);

            if (!transactionId.StartsWith("PAY-"))
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Invalid PayPal transaction ID format";
                return result;
            }

            result.Success = true;
            result.Status = PaymentStatus.Refunded;
            result.ProviderData.Add("paypal_refund_id", result.TransactionId);
            result.ProviderData.Add("original_payment_id", transactionId);
            result.ProviderData.Add("refund_reason", "requested_by_customer");

            _logger.LogInformation("PayPal refund successful: {RefundId} for original {TransactionId}", 
                result.TransactionId, transactionId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal refund error for transaction {TransactionId}", transactionId);
            result.Success = false;
            result.Status = PaymentStatus.Failed;
            result.ErrorMessage = "PayPal refund processing error";
            return result;
        }
    }

    public async Task<bool> ValidatePaymentMethodAsync(PaymentMethod paymentMethod)
    {
        _logger.LogInformation("Validating PayPal payment method: {Type}", paymentMethod.Type);

        // Simulate PayPal payment method validation
        await Task.Delay(400); // PayPal validation is typically slower

        return paymentMethod.Type switch
        {
            PaymentType.DigitalWallet => ValidatePayPalWallet(paymentMethod),
            PaymentType.CreditCard => ValidatePayPalCreditCard(paymentMethod),
            PaymentType.BankTransfer => ValidateBankTransfer(paymentMethod),
            _ => false
        };
    }

    public async Task<PaymentStatus> GetPaymentStatusAsync(string transactionId)
    {
        _logger.LogInformation("Checking PayPal payment status for transaction: {TransactionId}", transactionId);

        // Simulate PayPal API status check
        await Task.Delay(250);

        if (!transactionId.StartsWith("PAY-"))
        {
            return PaymentStatus.Failed;
        }

        // PayPal has more intermediate states
        if (transactionId.Contains("PENDING"))
            return PaymentStatus.Pending;
        if (transactionId.Contains("PROCESSING"))
            return PaymentStatus.Processing;
        if (transactionId.Contains("DENIED"))
            return PaymentStatus.Failed;
        if (transactionId.Contains("CANCELLED"))
            return PaymentStatus.Cancelled;

        return PaymentStatus.Completed;
    }

    public string GetProviderName()
    {
        return "PayPal";
    }

    public decimal GetProcessingFee(decimal amount)
    {
        // PayPal's standard pricing: 3.49% + $0.49
        return Math.Round(amount * _processingFeeRate + 0.49m, 2);
    }

    private bool ValidatePayPalWallet(PaymentMethod paymentMethod)
    {
        // PayPal wallet token validation
        return !string.IsNullOrWhiteSpace(paymentMethod.Token) &&
               paymentMethod.Token.StartsWith("paypal_");
    }

    private bool ValidatePayPalCreditCard(PaymentMethod paymentMethod)
    {
        // PayPal credit card processing validation
        return !string.IsNullOrWhiteSpace(paymentMethod.Token) &&
               !string.IsNullOrWhiteSpace(paymentMethod.Last4Digits) &&
               paymentMethod.Last4Digits.Length == 4;
    }

    private bool ValidateBankTransfer(PaymentMethod paymentMethod)
    {
        // PayPal bank transfer validation
        return !string.IsNullOrWhiteSpace(paymentMethod.Token) &&
               paymentMethod.Token.StartsWith("bank_");
    }
}
