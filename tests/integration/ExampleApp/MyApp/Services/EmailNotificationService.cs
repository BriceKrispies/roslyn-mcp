using Microsoft.Extensions.Logging;

namespace MyApp.Services;

/// <summary>
/// Email-based implementation of INotificationService.
/// This implementation sends notifications via email using SMTP or email service providers.
/// </summary>
public class EmailNotificationService : INotificationService
{
    private readonly ILogger<EmailNotificationService> _logger;
    private readonly string _smtpServer;
    private readonly int _smtpPort;

    public EmailNotificationService(ILogger<EmailNotificationService> logger)
    {
        _logger = logger;
        _smtpServer = "smtp.example.com"; // In real app, this would come from configuration
        _smtpPort = 587;
    }

    public async Task<bool> SendNotificationAsync(string recipient, string subject, string message)
    {
        _logger.LogInformation("Sending email notification to: {Recipient}, Subject: {Subject}", recipient, subject);
        
        // Simulate email sending delay
        await Task.Delay(100);
        
        // Simulate occasional failures for testing
        if (recipient.Contains("fail"))
        {
            _logger.LogError("Failed to send email to {Recipient}", recipient);
            return false;
        }

        _logger.LogInformation("Email sent successfully to {Recipient} via SMTP", recipient);
        return true;
    }

    public async Task<bool> SendBulkNotificationAsync(IEnumerable<string> recipients, string subject, string message)
    {
        _logger.LogInformation("Sending bulk email notification to {Count} recipients", recipients.Count());
        
        var tasks = recipients.Select(async recipient =>
        {
            return await SendNotificationAsync(recipient, subject, message);
        });

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(r => r);
        
        _logger.LogInformation("Bulk email completed: {SuccessCount}/{TotalCount} sent successfully", 
            successCount, recipients.Count());
        
        return successCount == recipients.Count();
    }

    public Task<bool> IsServiceAvailableAsync()
    {
        // Simulate SMTP server connectivity check
        _logger.LogInformation("Checking SMTP server availability: {Server}:{Port}", _smtpServer, _smtpPort);
        return Task.FromResult(true); // Simulate always available for testing
    }

    public string GetServiceType()
    {
        return "Email (SMTP)";
    }

    public async Task<NotificationResult> SendWithResultAsync(string recipient, string subject, string message)
    {
        _logger.LogInformation("Sending email with detailed result to: {Recipient}", recipient);
        
        var result = new NotificationResult
        {
            Channel = GetServiceType(),
            SentAt = DateTime.UtcNow
        };

        try
        {
            // Simulate email sending
            await Task.Delay(150);
            
            if (recipient.Contains("fail"))
            {
                result.Success = false;
                result.ErrorMessage = "SMTP delivery failed - invalid recipient";
                return result;
            }

            result.Success = true;
            result.MessageId = $"email_{Guid.NewGuid():N}";
            
            _logger.LogInformation("Email sent with message ID: {MessageId}", result.MessageId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while sending email to {Recipient}", recipient);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }
}
