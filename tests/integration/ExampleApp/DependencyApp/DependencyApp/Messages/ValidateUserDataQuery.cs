using MediatR;

namespace DependencyApp.Messages;

/// <summary>
/// Query to validate user data against shared business rules.
/// This demonstrates cross-project MediatR request/response patterns for LSP testing.
/// </summary>
public record ValidateUserDataQuery(
    string UserId,
    string? Name,
    string? Email,
    string? Bio,
    ValidationLevel Level = ValidationLevel.Standard
) : IRequest<UserDataValidationResult>;

/// <summary>
/// Validation levels for different scenarios
/// </summary>
public enum ValidationLevel
{
    Basic,
    Standard,
    Strict,
    Enterprise
}

/// <summary>
/// Result of user data validation with detailed feedback
/// </summary>
public class UserDataValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public ValidationLevel AppliedLevel { get; set; }
    public Dictionary<string, object> ValidationMetadata { get; set; } = new();
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
    public string ValidationSource { get; set; } = "DependencyApp.ValidationService";
}

