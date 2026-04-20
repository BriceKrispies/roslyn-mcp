namespace MyApp.Services;

/// <summary>
/// Service interface for sending notifications through various channels.
/// Multiple implementations demonstrate different notification strategies.
/// </summary>
public interface INotificationService
{
    Task<bool> SendNotificationAsync(string recipient, string subject, string message);
    Task<bool> SendBulkNotificationAsync(IEnumerable<string> recipients, string subject, string message);
    Task<bool> IsServiceAvailableAsync();
    string GetServiceType();
    Task<NotificationResult> SendWithResultAsync(string recipient, string subject, string message);
}

public class NotificationResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime SentAt { get; set; }
    public string Channel { get; set; } = string.Empty;
}
