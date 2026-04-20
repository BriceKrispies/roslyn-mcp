using MediatR;
using Microsoft.Extensions.Logging;
using MyApp.Messages;

namespace MyApp.Handlers;

public class ProcessUserActionCommandHandler : IRequestHandler<ProcessUserActionCommand, UserActionResult>
{
    private readonly IMediator _mediator;
    private readonly ILogger<ProcessUserActionCommandHandler> _logger;

    public ProcessUserActionCommandHandler(IMediator mediator, ILogger<ProcessUserActionCommandHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<UserActionResult> Handle(ProcessUserActionCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing user action: {ActionType} for user: {UserId}, Priority: {IsHighPriority}", 
            request.ActionType, request.UserId, request.IsHighPriority);

        var result = new UserActionResult();

        try
        {
            // BRANCH 1: Authentication-related actions
            if (request.ActionType.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase))
            {
                result = await HandleAuthenticationAction(request, cancellationToken);
            }
            // BRANCH 2: Profile-related actions  
            else if (request.ActionType.StartsWith("PROFILE", StringComparison.OrdinalIgnoreCase))
            {
                result = await HandleProfileAction(request, cancellationToken);
            }
            // BRANCH 3: Maintenance actions (high priority only)
            else if (request.ActionType.StartsWith("MAINTENANCE", StringComparison.OrdinalIgnoreCase))
            {
                result = await HandleMaintenanceAction(request, cancellationToken);
            }
            // BRANCH 4: Default action logging
            else
            {
                result = await HandleGenericAction(request, cancellationToken);
            }

            // Always log the action regardless of branch - THIS SAVES TO DATABASE
            await _mediator.Send(new LogUserActivityCommand(
                request.UserId, 
                $"ACTION_{request.ActionType}", 
                $"Result: {result.Success}, Data: {request.Data ?? "N/A"}"), 
                cancellationToken);

            result.Success = true;
            _logger.LogInformation("Successfully processed {ActionType} for user {UserId}", request.ActionType, request.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process action {ActionType} for user {UserId}", request.ActionType, request.UserId);
            
            // Log error - THIS ALSO SAVES TO DATABASE
            await _mediator.Send(new LogUserActivityCommand(
                request.UserId, 
                $"ACTION_ERROR_{request.ActionType}", 
                $"Error: {ex.Message}"), 
                cancellationToken);
                
            result.Success = false;
            result.Message = $"Action failed: {ex.Message}";
        }

        return result;
    }

    private async Task<UserActionResult> HandleAuthenticationAction(ProcessUserActionCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling authentication action: {ActionType}", request.ActionType);
        
        var result = new UserActionResult();

        if (request.ActionType.Equals("AUTH_LOGIN", StringComparison.OrdinalIgnoreCase))
        {
            // Create new session - THIS SAVES TO DATABASE
            var sessionToken = await _mediator.Send(new CreateUserSessionCommand(
                request.UserId, 
                request.Data, // IP Address
                "ExampleApp/1.0"), 
                cancellationToken);
                
            result.SessionToken = sessionToken;
            result.Message = "Login successful";
            
            // High priority actions get extended sessions
            if (request.IsHighPriority)
            {
                result.Metadata["SessionType"] = "Extended";
                result.Metadata["ExpirationHours"] = 24;
            }
        }
        else if (request.ActionType.Equals("AUTH_LOGOUT", StringComparison.OrdinalIgnoreCase))
        {
            // Logout just logs activity - already handled by main flow
            result.Message = "Logout successful";
        }

        return result;
    }

    private async Task<UserActionResult> HandleProfileAction(ProcessUserActionCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling profile action: {ActionType}", request.ActionType);
        
        var result = new UserActionResult();

        if (request.ActionType.Equals("PROFILE_UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            // Update user profile - THIS MODIFIES DATABASE
            var updateSuccess = await _mediator.Send(new UpdateUserProfileCommand(
                request.UserId,
                request.Data, // New name or bio
                NotifyUser: request.IsHighPriority), 
                cancellationToken);
                
            result.Message = updateSuccess ? "Profile updated successfully" : "Profile update failed";
            result.Metadata["ProfileUpdateSuccess"] = updateSuccess;
        }

        return result;
    }

    private async Task<UserActionResult> HandleMaintenanceAction(ProcessUserActionCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling maintenance action: {ActionType}, Priority: {IsHighPriority}", request.ActionType, request.IsHighPriority);
        
        var result = new UserActionResult();

        // Only allow maintenance for high priority requests
        if (!request.IsHighPriority)
        {
            result.Message = "Maintenance actions require high priority flag";
            return result;
        }

        // Perform maintenance - THIS DOES BULK DATABASE OPERATIONS
        var maintenanceResult = await _mediator.Send(new PerformMaintenanceCommand(
            request.UserId,
            request.ActionType,
            ForceExecution: true), 
            cancellationToken);
            
        result.Message = maintenanceResult.Success 
            ? $"Maintenance completed, {maintenanceResult.RecordsAffected} records affected"
            : $"Maintenance failed: {maintenanceResult.ErrorMessage}";
        result.Metadata["RecordsAffected"] = maintenanceResult.RecordsAffected;
        result.Metadata["Duration"] = maintenanceResult.Duration.TotalSeconds;

        return result;
    }

    private async Task<UserActionResult> HandleGenericAction(ProcessUserActionCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling generic action: {ActionType}", request.ActionType);
        
        var result = new UserActionResult
        {
            Message = $"Generic action {request.ActionType} processed"
        };

        // For unknown actions, just return basic result
        // The main logging will still occur in the parent method
        await Task.CompletedTask;
        
        return result;
    }
}
