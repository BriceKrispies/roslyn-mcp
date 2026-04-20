using DependencyApp.Messages;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DependencyApp.Handlers;

/// <summary>
/// Handler for validating user data against shared business rules.
/// This demonstrates cross-project MediatR handler patterns for LSP testing.
/// </summary>
public class ValidateUserDataQueryHandler : IRequestHandler<ValidateUserDataQuery, UserDataValidationResult>
{
    private readonly ILogger<ValidateUserDataQueryHandler> _logger;

    public ValidateUserDataQueryHandler(ILogger<ValidateUserDataQueryHandler> logger)
    {
        _logger = logger;
    }

    public async Task<UserDataValidationResult> Handle(ValidateUserDataQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Validating user data for user {UserId} with level {ValidationLevel}", 
            request.UserId, request.Level);

        var result = new UserDataValidationResult
        {
            AppliedLevel = request.Level,
            ValidationMetadata = new()
            {
                ["RequestedBy"] = request.UserId,
                ["ValidationRules"] = GetValidationRulesForLevel(request.Level)
            }
        };

        await Task.Delay(50, cancellationToken); // Simulate async validation work

        // Validate user ID
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            result.Errors.Add("UserId is required and cannot be empty");
        }
        else if (request.UserId.Length < 3)
        {
            result.Errors.Add("UserId must be at least 3 characters long");
        }

        // Validate name based on level
        await ValidateName(request, result, cancellationToken);

        // Validate email based on level
        await ValidateEmail(request, result, cancellationToken);

        // Validate bio based on level
        await ValidateBio(request, result, cancellationToken);

        // Apply level-specific validations
        await ApplyLevelSpecificValidations(request, result, cancellationToken);

        result.IsValid = result.Errors.Count == 0;

        _logger.LogInformation("User data validation completed for {UserId}: Valid={IsValid}, Errors={ErrorCount}, Warnings={WarningCount}", 
            request.UserId, result.IsValid, result.Errors.Count, result.Warnings.Count);

        return result;
    }

    private async Task ValidateName(ValidateUserDataQuery request, UserDataValidationResult result, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Make async for consistency

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            if (request.Level >= ValidationLevel.Standard)
            {
                result.Errors.Add("Name is required for Standard validation and above");
            }
            else
            {
                result.Warnings.Add("Name is recommended but not required for Basic validation");
            }
            return;
        }

        if (request.Name.Length < 2)
        {
            result.Errors.Add("Name must be at least 2 characters long");
        }
        else if (request.Name.Length > 100)
        {
            result.Errors.Add("Name cannot exceed 100 characters");
        }

        // Check for invalid characters in strict mode
        if (request.Level >= ValidationLevel.Strict)
        {
            if (request.Name.Any(c => char.IsDigit(c)))
            {
                result.Warnings.Add("Name contains numbers, which may not be appropriate");
            }

            if (request.Name.Any(c => "!@#$%^&*()".Contains(c)))
            {
                result.Errors.Add("Name contains invalid special characters");
            }
        }
    }

    private async Task ValidateEmail(ValidateUserDataQuery request, UserDataValidationResult result, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Make async for consistency

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            if (request.Level >= ValidationLevel.Standard)
            {
                result.Errors.Add("Email is required for Standard validation and above");
            }
            return;
        }

        // Basic email validation
        if (!request.Email.Contains('@'))
        {
            result.Errors.Add("Email must contain @ symbol");
            return;
        }

        var parts = request.Email.Split('@');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            result.Errors.Add("Email format is invalid");
            return;
        }

        // Strict email validation
        if (request.Level >= ValidationLevel.Strict)
        {
            if (!parts[1].Contains('.'))
            {
                result.Errors.Add("Email domain must contain a period");
            }

            if (request.Email.Length > 254)
            {
                result.Errors.Add("Email address is too long (max 254 characters)");
            }
        }

        // Enterprise email validation
        if (request.Level == ValidationLevel.Enterprise)
        {
            var blockedDomains = new[] { "tempmail.com", "10minutemail.com", "guerrillamail.com" };
            if (blockedDomains.Any(domain => parts[1].EndsWith(domain, StringComparison.OrdinalIgnoreCase)))
            {
                result.Errors.Add("Email domain is not allowed in enterprise mode");
            }
        }
    }

    private async Task ValidateBio(ValidateUserDataQuery request, UserDataValidationResult result, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Make async for consistency

        if (string.IsNullOrWhiteSpace(request.Bio))
        {
            return; // Bio is always optional
        }

        if (request.Bio.Length > 500)
        {
            result.Errors.Add("Bio cannot exceed 500 characters");
        }

        // Check for inappropriate content in strict mode
        if (request.Level >= ValidationLevel.Strict)
        {
            var inappropriateWords = new[] { "spam", "scam", "fake" };
            if (inappropriateWords.Any(word => request.Bio.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                result.Warnings.Add("Bio may contain inappropriate content");
            }
        }
    }

    private async Task ApplyLevelSpecificValidations(ValidateUserDataQuery request, UserDataValidationResult result, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Make async for consistency

        switch (request.Level)
        {
            case ValidationLevel.Enterprise:
                result.ValidationMetadata["ComplianceCheck"] = "GDPR,SOX,HIPAA";
                if (!string.IsNullOrEmpty(request.Email) && !request.Email.EndsWith(".com") && !request.Email.EndsWith(".org"))
                {
                    result.Warnings.Add("Enterprise mode prefers .com or .org email domains");
                }
                break;

            case ValidationLevel.Strict:
                result.ValidationMetadata["SecurityLevel"] = "High";
                break;

            case ValidationLevel.Standard:
                result.ValidationMetadata["RecommendedFields"] = "Name,Email";
                break;

            case ValidationLevel.Basic:
                result.ValidationMetadata["MinimalValidation"] = true;
                break;
        }
    }

    private static string[] GetValidationRulesForLevel(ValidationLevel level) => level switch
    {
        ValidationLevel.Basic => new[] { "UserId required" },
        ValidationLevel.Standard => new[] { "UserId required", "Name required", "Email required", "Basic format checks" },
        ValidationLevel.Strict => new[] { "All Standard rules", "Character validation", "Content filtering", "Length limits" },
        ValidationLevel.Enterprise => new[] { "All Strict rules", "Domain validation", "Compliance checks", "Security scanning" },
        _ => new[] { "Unknown validation level" }
    };
}

