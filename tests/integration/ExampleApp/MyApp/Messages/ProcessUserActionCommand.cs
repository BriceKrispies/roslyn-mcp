using MediatR;

namespace MyApp.Messages;

public record ProcessUserActionCommand(
    string UserId, 
    string ActionType, 
    string? Data = null,
    bool IsHighPriority = false) : IRequest<UserActionResult>;

public class UserActionResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? SessionToken { get; set; }
    public int? ActivityLogId { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
