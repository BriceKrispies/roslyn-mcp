using Microsoft.Extensions.Logging;

namespace MyApp.Services;

/// <summary>
/// Mock payment processor implementation of IPaymentProcessor.
/// This implementation provides predictable behavior for testing and development.
/// </summary>
public class MockPaymentProcessor : IPaymentProcessor
{
    private readonly ILogger<MockPaymentProcessor> _logger;
    private readonly Dictionary<string, PaymentResult> _transactionHistory;
    private readonly decimal _processingFeeRate;

    public MockPaymentProcessor(ILogger<MockPaymentProcessor> logger)
    {
        _logger = logger;
        _transactionHistory = new Dictionary<string, PaymentResult>();
        _processingFeeRate = 0.025m; // 2.5% flat rate for mock
    }

    public async Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request)
    {
        _logger.LogInformation("Mock: Processing payment of {Amount} {Currency}", 
            request.Amount, request.Currency);

        var result = new PaymentResult
        {
            ProcessedAt = DateTime.UtcNow,
            ProcessedAmount = request.Amount,
            ProcessingFee = GetProcessingFee(request.Amount)
        };

        // Simulate network delay
        await Task.Delay(100);

        try
        {
            // Deterministic test scenarios based on payment method token
            if (request.PaymentMethod.Token.Contains("decline"))
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Mock: Payment method declined";
                result.TransactionId = $"mock_declined_{DateTime.UtcNow.Ticks}";
            }
            else if (request.PaymentMethod.Token.Contains("timeout"))
            {
                await Task.Delay(5000); // Simulate timeout
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Mock: Payment processing timeout";
                result.TransactionId = $"mock_timeout_{DateTime.UtcNow.Ticks}";
            }
            else if (request.PaymentMethod.Token.Contains("pending"))
            {
                result.Success = true;
                result.Status = PaymentStatus.Pending;
                result.TransactionId = $"mock_pending_{DateTime.UtcNow.Ticks}";
            }
            else if (request.Amount <= 0)
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Mock: Invalid payment amount";
                result.TransactionId = $"mock_invalid_{DateTime.UtcNow.Ticks}";
            }
            else
            {
                // Default success case
                result.Success = true;
                result.Status = PaymentStatus.Completed;
                result.TransactionId = $"mock_success_{DateTime.UtcNow.Ticks}";
            }

            // Add mock provider data
            result.ProviderData.Add("mock_reference", result.TransactionId);
            result.ProviderData.Add("mock_test_mode", "true");
            result.ProviderData.Add("mock_customer_email", request.CustomerEmail);

            // Store in history for status checks
            _transactionHistory[result.TransactionId] = result;

            _logger.LogInformation("Mock payment result: {Success}, Transaction: {TransactionId}", 
                result.Success, result.TransactionId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mock payment processing error for amount {Amount}", request.Amount);
            result.Success = false;
            result.Status = PaymentStatus.Failed;
            result.ErrorMessage = $"Mock: Exception occurred - {ex.Message}";
            result.TransactionId = $"mock_error_{DateTime.UtcNow.Ticks}";
            return result;
        }
    }

    public async Task<PaymentResult> RefundPaymentAsync(string transactionId, decimal amount)
    {
        _logger.LogInformation("Mock: Processing refund for transaction {TransactionId}, amount {Amount}", 
            transactionId, amount);

        var result = new PaymentResult
        {
            ProcessedAt = DateTime.UtcNow,
            ProcessedAmount = amount,
            TransactionId = $"mock_refund_{DateTime.UtcNow.Ticks}"
        };

        // Simulate network delay
        await Task.Delay(50);

        try
        {
            if (!_transactionHistory.ContainsKey(transactionId))
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Mock: Original transaction not found";
                return result;
            }

            var originalPayment = _transactionHistory[transactionId];
            if (originalPayment.Status != PaymentStatus.Completed)
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Mock: Cannot refund non-completed payment";
                return result;
            }

            if (amount > originalPayment.ProcessedAmount)
            {
                result.Success = false;
                result.Status = PaymentStatus.Failed;
                result.ErrorMessage = "Mock: Refund amount exceeds original payment";
                return result;
            }

            result.Success = true;
            result.Status = PaymentStatus.Refunded;
            result.ProviderData.Add("mock_refund_reference", result.TransactionId);
            result.ProviderData.Add("original_transaction", transactionId);

            // Update transaction history
            _transactionHistory[result.TransactionId] = result;

            _logger.LogInformation("Mock refund successful: {RefundId} for original {TransactionId}", 
                result.TransactionId, transactionId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mock refund error for transaction {TransactionId}", transactionId);
            result.Success = false;
            result.Status = PaymentStatus.Failed;
            result.ErrorMessage = $"Mock: Refund exception - {ex.Message}";
            return result;
        }
    }

    public Task<bool> ValidatePaymentMethodAsync(PaymentMethod paymentMethod)
    {
        _logger.LogInformation("Mock: Validating payment method: {Type}", paymentMethod.Type);

        // Mock validation - simple rules for testing
        var isValid = paymentMethod.Type switch
        {
            PaymentType.CreditCard => !paymentMethod.Token.Contains("invalid") && 
                                     paymentMethod.Last4Digits.Length == 4,
            PaymentType.DebitCard => !paymentMethod.Token.Contains("invalid"),
            PaymentType.DigitalWallet => paymentMethod.Token.Contains("wallet"),
            PaymentType.BankTransfer => paymentMethod.Token.Contains("bank"),
            PaymentType.Cryptocurrency => paymentMethod.Token.Contains("crypto"),
            _ => false
        };

        return Task.FromResult(isValid);
    }

    public Task<PaymentStatus> GetPaymentStatusAsync(string transactionId)
    {
        _logger.LogInformation("Mock: Checking payment status for transaction: {TransactionId}", transactionId);

        if (_transactionHistory.TryGetValue(transactionId, out var payment))
        {
            return Task.FromResult(payment.Status);
        }

        // If not in history, determine status from transaction ID patterns
        if (transactionId.Contains("pending"))
            return Task.FromResult(PaymentStatus.Pending);
        if (transactionId.Contains("declined") || transactionId.Contains("error") || transactionId.Contains("invalid"))
            return Task.FromResult(PaymentStatus.Failed);
        if (transactionId.Contains("refund"))
            return Task.FromResult(PaymentStatus.Refunded);
        if (transactionId.Contains("success"))
            return Task.FromResult(PaymentStatus.Completed);

        return Task.FromResult(PaymentStatus.Failed); // Default for unknown transactions
    }

    public string GetProviderName()
    {
        return "Mock Payment Processor";
    }

    public decimal GetProcessingFee(decimal amount)
    {
        // Simple flat rate for mock processor
        return Math.Round(amount * _processingFeeRate, 2);
    }

    /// <summary>
    /// Mock-specific method to get transaction history for testing
    /// </summary>
    public IReadOnlyDictionary<string, PaymentResult> GetTransactionHistory()
    {
        return _transactionHistory.AsReadOnly();
    }

    /// <summary>
    /// Mock-specific method to clear transaction history for clean testing
    /// </summary>
    public void ClearTransactionHistory()
    {
        _transactionHistory.Clear();
        _logger.LogInformation("Mock: Transaction history cleared");
    }
}
