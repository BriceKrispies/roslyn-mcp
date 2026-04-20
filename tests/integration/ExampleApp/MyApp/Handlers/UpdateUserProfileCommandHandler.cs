using DependencyApp.Messages;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyApp.Data;
using MyApp.Messages;

namespace MyApp.Handlers;

public class UpdateUserProfileCommandHandler : IRequestHandler<UpdateUserProfileCommand, bool>
{
    private readonly ApplicationDbContext _context;
    private readonly IMediator _mediator;
    private readonly ILogger<UpdateUserProfileCommandHandler> _logger;

    public UpdateUserProfileCommandHandler(
        ApplicationDbContext context, 
        IMediator mediator,
        ILogger<UpdateUserProfileCommandHandler> logger)
    {
        _context = context;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<bool> Handle(UpdateUserProfileCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating profile for user: {UserId}, Notify: {NotifyUser}", 
            request.UserId, request.NotifyUser);

        try
        {
            // Find user by string ID (convert to int)
            if (!int.TryParse(request.UserId, out var userIdInt))
            {
                _logger.LogWarning("Invalid user ID format: {UserId}", request.UserId);
                return false;
            }

            // CRITICAL: THIS IS A DATABASE READ OPERATION
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userIdInt, cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("User not found: {UserId}", request.UserId);
                return false;
            }

            // CROSS-PROJECT VALIDATION: Use DependencyApp to validate user data
            _logger.LogInformation("Validating user data using shared validation service...");
            var validationQuery = new ValidateUserDataQuery(
                request.UserId,
                request.Name ?? user.Name,
                user.Email,
                request.Bio ?? user.Bio,
                ValidationLevel.Standard // Use standard validation for profile updates
            );

            var validationResult = await _mediator.Send(validationQuery, cancellationToken);
            
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("User data validation failed for {UserId}: {Errors}", 
                    request.UserId, string.Join(", ", validationResult.Errors));
                
                await _mediator.Send(new LogUserActivityCommand(
                    request.UserId,
                    "PROFILE_UPDATE_VALIDATION_FAILED",
                    $"Validation errors: {string.Join(", ", validationResult.Errors)}"),
                    cancellationToken);
                
                return false;
            }

            if (validationResult.Warnings.Any())
            {
                _logger.LogInformation("User data validation completed with warnings for {UserId}: {Warnings}", 
                    request.UserId, string.Join(", ", validationResult.Warnings));
            }

            _logger.LogInformation("User data validation passed for {UserId} using {ValidationSource}", 
                request.UserId, validationResult.ValidationSource);

            // Track what changed for logging
            var changes = new List<string>();

            // Update fields if provided
            if (!string.IsNullOrEmpty(request.Name) && user.Name != request.Name)
            {
                var oldName = user.Name;
                user.Name = request.Name;
                changes.Add($"Name: {oldName} → {request.Name}");
            }

            if (!string.IsNullOrEmpty(request.Bio) && user.Bio != request.Bio)
            {
                var oldBio = user.Bio ?? "None";
                user.Bio = request.Bio;
                changes.Add($"Bio: {oldBio} → {request.Bio}");
            }

            if (changes.Any())
            {
                user.UpdatedAt = DateTime.UtcNow;

                // CRITICAL: THIS IS A DATABASE WRITE OPERATION - UPDATE USER
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Successfully updated user {UserId}. Changes: {Changes}", 
                    request.UserId, string.Join(", ", changes));

                // Log the profile update activity - ANOTHER DATABASE WRITE
                await _mediator.Send(new LogUserActivityCommand(
                    request.UserId,
                    "PROFILE_UPDATED",
                    $"Updated fields: {string.Join(", ", changes)}"),
                    cancellationToken);

                // If notification requested, log that too - ANOTHER DATABASE WRITE
                if (request.NotifyUser)
                {
                    await _mediator.Send(new LogUserActivityCommand(
                        request.UserId,
                        "USER_NOTIFIED",
                        "Profile update notification sent"),
                        cancellationToken);
                }

                return true;
            }
            else
            {
                _logger.LogDebug("No changes made to user {UserId} profile", request.UserId);
                return true; // No changes needed, but not an error
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update profile for user: {UserId}", request.UserId);
            
            // Log the error - DATABASE WRITE FOR ERROR TRACKING
            await _mediator.Send(new LogUserActivityCommand(
                request.UserId,
                "PROFILE_UPDATE_ERROR",
                $"Error: {ex.Message}"),
                cancellationToken);

            return false;
        }
    }
}
