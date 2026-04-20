using Microsoft.Extensions.Logging;

namespace MyApp.Services;

/// <summary>
/// SMS-based implementation of INotificationService.
/// This implementation sends notifications via SMS using services like Twilio or AWS SNS.
/// </summary>
public class SmsNotificationService : INotificationService
{
    private readonly ILogger<SmsNotificationService> _logger;
    private readonly string _apiKey;
    private readonly string _serviceProvider;

    public SmsNotificationService(ILogger<SmsNotificationService> logger)
    {
        _logger = logger;
        _apiKey = "fake_api_key_12345"; // In real app, this would come from secure configuration
        _serviceProvider = "TwilioSMS";
    }

    public async Task<bool> SendNotificationAsync(string recipient, string subject, string message)
    {
        _logger.LogInformation("Sending SMS notification to: {Recipient}", recipient);
        
        // Validate phone number format (basic validation)
        if (!IsValidPhoneNumber(recipient))
        {
            _logger.LogError("Invalid phone number format: {Recipient}", recipient);
            return false;
        }

        // Simulate SMS API call delay
        await Task.Delay(200);
        
        // Simulate message length restrictions
        var combinedMessage = $"{subject}: {message}";
        if (combinedMessage.Length > 160)
        {
            combinedMessage = combinedMessage.Substring(0, 157) + "...";
        }

        // Simulate occasional failures
        if (recipient.Contains("555000"))
        {
            _logger.LogError("SMS delivery failed for {Recipient} - blocked number", recipient);
            return false;
        }

        _logger.LogInformation("SMS sent successfully to {Recipient} via {Provider}", recipient, _serviceProvider);
        return true;
    }

    public async Task<bool> SendBulkNotificationAsync(IEnumerable<string> recipients, string subject, string message)
    {
        _logger.LogInformation("Sending bulk SMS notification to {Count} recipients", recipients.Count());
        
        // SMS services often have rate limits, so we might need to batch
        var batchSize = 10;
        var batches = recipients.Chunk(batchSize);
        var allSuccessful = true;

        foreach (var batch in batches)
        {
            var tasks = batch.Select(async recipient =>
            {
                return await SendNotificationAsync(recipient, subject, message);
            });

            var results = await Task.WhenAll(tasks);
            if (results.Any(r => !r))
            {
                allSuccessful = false;
            }

            // Rate limiting delay between batches
            await Task.Delay(1000);
        }

        _logger.LogInformation("Bulk SMS completed with overall success: {Success}", allSuccessful);
        return allSuccessful;
    }

    public async Task<bool> IsServiceAvailableAsync()
    {
        _logger.LogInformation("Checking SMS service availability with provider: {Provider}", _serviceProvider);
        
        // Simulate API health check
        await Task.Delay(50);
        
        // Simulate 99% uptime
        return Random.Shared.NextDouble() > 0.01;
    }

    public string GetServiceType()
    {
        return $"SMS ({_serviceProvider})";
    }

    public async Task<NotificationResult> SendWithResultAsync(string recipient, string subject, string message)
    {
        _logger.LogInformation("Sending SMS with detailed result to: {Recipient}", recipient);
        
        var result = new NotificationResult
        {
            Channel = GetServiceType(),
            SentAt = DateTime.UtcNow
        };

        try
        {
            if (!IsValidPhoneNumber(recipient))
            {
                result.Success = false;
                result.ErrorMessage = "Invalid phone number format";
                return result;
            }

            // Simulate SMS API call
            await Task.Delay(200);
            
            if (recipient.Contains("555000"))
            {
                result.Success = false;
                result.ErrorMessage = "SMS delivery failed - blocked number";
                return result;
            }

            result.Success = true;
            result.MessageId = $"sms_{Random.Shared.Next(100000, 999999)}";
            
            _logger.LogInformation("SMS sent with message ID: {MessageId}", result.MessageId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while sending SMS to {Recipient}", recipient);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private static bool IsValidPhoneNumber(string phoneNumber)
    {
        // Basic phone number validation - in real app would use proper validation
        return !string.IsNullOrWhiteSpace(phoneNumber) && 
               phoneNumber.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "")
                          .All(c => char.IsDigit(c) || c == '+') &&
               phoneNumber.Length >= 10;
    }
}
