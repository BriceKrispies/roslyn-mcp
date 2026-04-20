using MediatR;
using Microsoft.Extensions.Logging;
using MyApp.Data;
using MyApp.Messages;
using MyApp.Models;

namespace MyApp.Handlers;

public class LogUserActivityCommandHandler : IRequestHandler<LogUserActivityCommand>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<LogUserActivityCommandHandler> _logger;

    public LogUserActivityCommandHandler(ApplicationDbContext context, ILogger<LogUserActivityCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Handle(LogUserActivityCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("USER ACTIVITY - User: {UserId}, Activity: {Activity}, Details: {Details}", 
            request.UserId, request.Activity, request.Details ?? "N/A");

        try
        {
            // Save activity to database - THIS IS A DATABASE WRITE OPERATION
            var activity = new UserActivity
            {
                UserId = request.UserId,
                Activity = request.Activity,
                Details = request.Details,
                Timestamp = DateTime.UtcNow,
                IsSuccessful = true
            };

            _context.UserActivities.Add(activity);
            await _context.SaveChangesAsync(cancellationToken);
            
            _logger.LogDebug("Successfully saved user activity to database with ID: {ActivityId}", activity.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save user activity to database for user: {UserId}", request.UserId);
            
            // Save error activity to database
            var errorActivity = new UserActivity
            {
                UserId = request.UserId,
                Activity = "ACTIVITY_LOG_ERROR",
                Details = $"Failed to log activity: {request.Activity}",
                Timestamp = DateTime.UtcNow,
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };

            try
            {
                _context.UserActivities.Add(errorActivity);
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                _logger.LogCritical(saveEx, "Critical: Failed to save error activity to database");
            }
        }
    }
}
