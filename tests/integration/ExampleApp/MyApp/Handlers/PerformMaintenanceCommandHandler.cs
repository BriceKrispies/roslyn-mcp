using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyApp.Data;
using MyApp.Messages;
using System.Diagnostics;

namespace MyApp.Handlers;

public class PerformMaintenanceCommandHandler : IRequestHandler<PerformMaintenanceCommand, MaintenanceResult>
{
    private readonly ApplicationDbContext _context;
    private readonly IMediator _mediator;
    private readonly ILogger<PerformMaintenanceCommandHandler> _logger;

    public PerformMaintenanceCommandHandler(
        ApplicationDbContext context,
        IMediator mediator,
        ILogger<PerformMaintenanceCommandHandler> logger)
    {
        _context = context;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<MaintenanceResult> Handle(PerformMaintenanceCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting maintenance operation: {MaintenanceType} for user: {UserId}, Force: {ForceExecution}",
            request.MaintenanceType, request.UserId, request.ForceExecution);

        var stopwatch = Stopwatch.StartNew();
        var result = new MaintenanceResult();

        try
        {
            // Log maintenance start - DATABASE WRITE
            await _mediator.Send(new LogUserActivityCommand(
                request.UserId,
                $"MAINTENANCE_START_{request.MaintenanceType}",
                $"Force: {request.ForceExecution}"),
                cancellationToken);

            switch (request.MaintenanceType.ToUpperInvariant())
            {
                case "MAINTENANCE_CLEANUP_ACTIVITIES":
                    result = await CleanupOldActivities(request, cancellationToken);
                    break;
                    
                case "MAINTENANCE_CLEANUP_SESSIONS":
                    result = await CleanupExpiredSessions(request, cancellationToken);
                    break;
                    
                case "MAINTENANCE_UPDATE_STATISTICS":
                    result = await UpdateUserStatistics(request, cancellationToken);
                    break;
                    
                case "MAINTENANCE_FULL_CLEANUP":
                    result = await PerformFullCleanup(request, cancellationToken);
                    break;
                    
                default:
                    result.Success = false;
                    result.ErrorMessage = $"Unknown maintenance type: {request.MaintenanceType}";
                    break;
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            // Log maintenance completion - ANOTHER DATABASE WRITE
            await _mediator.Send(new LogUserActivityCommand(
                request.UserId,
                $"MAINTENANCE_COMPLETE_{request.MaintenanceType}",
                $"Success: {result.Success}, Records: {result.RecordsAffected}, Duration: {result.Duration.TotalSeconds:F2}s"),
                cancellationToken);

            _logger.LogInformation("Maintenance {MaintenanceType} completed in {Duration}ms. Success: {Success}, Records: {RecordsAffected}",
                request.MaintenanceType, stopwatch.ElapsedMilliseconds, result.Success, result.RecordsAffected);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Duration = stopwatch.Elapsed;

            _logger.LogError(ex, "Maintenance operation {MaintenanceType} failed for user {UserId}",
                request.MaintenanceType, request.UserId);

            // Log maintenance error - DATABASE WRITE
            await _mediator.Send(new LogUserActivityCommand(
                request.UserId,
                $"MAINTENANCE_ERROR_{request.MaintenanceType}",
                $"Error: {ex.Message}"),
                cancellationToken);
        }

        return result;
    }

    private async Task<MaintenanceResult> CleanupOldActivities(PerformMaintenanceCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cleaning up old activities for user: {UserId}", request.UserId);

        // CRITICAL: BULK DATABASE DELETE OPERATION
        var cutoffDate = DateTime.UtcNow.AddDays(-30); // Keep last 30 days
        var oldActivities = await _context.UserActivities
            .Where(a => a.UserId == request.UserId && a.Timestamp < cutoffDate)
            .ToListAsync(cancellationToken);

        if (oldActivities.Any())
        {
            _context.UserActivities.RemoveRange(oldActivities);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new MaintenanceResult
        {
            Success = true,
            RecordsAffected = oldActivities.Count
        };
    }

    private async Task<MaintenanceResult> CleanupExpiredSessions(PerformMaintenanceCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cleaning up expired sessions for user: {UserId}", request.UserId);

        // CRITICAL: BULK DATABASE DELETE OPERATION
        var expiredSessions = await _context.UserSessions
            .Where(s => s.UserId == request.UserId && 
                       (s.ExpiresAt < DateTime.UtcNow || !s.IsActive))
            .ToListAsync(cancellationToken);

        if (expiredSessions.Any())
        {
            _context.UserSessions.RemoveRange(expiredSessions);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new MaintenanceResult
        {
            Success = true,
            RecordsAffected = expiredSessions.Count
        };
    }

    private async Task<MaintenanceResult> UpdateUserStatistics(PerformMaintenanceCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating user statistics for user: {UserId}", request.UserId);

        int recordsAffected = 0;

        // Update user's last activity timestamp - DATABASE READ + WRITE
        if (int.TryParse(request.UserId, out var userIdInt))
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userIdInt, cancellationToken);

            if (user != null)
            {
                var lastActivity = await _context.UserActivities
                    .Where(a => a.UserId == request.UserId)
                    .OrderByDescending(a => a.Timestamp)
                    .FirstOrDefaultAsync(cancellationToken);

                if (lastActivity != null)
                {
                    user.UpdatedAt = lastActivity.Timestamp;
                    await _context.SaveChangesAsync(cancellationToken);
                    recordsAffected++;
                }
            }
        }

        // Update session access counts - BULK DATABASE UPDATE
        var activeSessions = await _context.UserSessions
            .Where(s => s.UserId == request.UserId && s.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var session in activeSessions)
        {
            session.LastAccessedAt = DateTime.UtcNow;
            recordsAffected++;
        }

        if (activeSessions.Any())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new MaintenanceResult
        {
            Success = true,
            RecordsAffected = recordsAffected
        };
    }

    private async Task<MaintenanceResult> PerformFullCleanup(PerformMaintenanceCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Performing full cleanup for user: {UserId}", request.UserId);

        int totalRecordsAffected = 0;

        // Combine all cleanup operations
        var activitiesResult = await CleanupOldActivities(request, cancellationToken);
        totalRecordsAffected += activitiesResult.RecordsAffected;

        var sessionsResult = await CleanupExpiredSessions(request, cancellationToken);
        totalRecordsAffected += sessionsResult.RecordsAffected;

        var statisticsResult = await UpdateUserStatistics(request, cancellationToken);
        totalRecordsAffected += statisticsResult.RecordsAffected;

        return new MaintenanceResult
        {
            Success = activitiesResult.Success && sessionsResult.Success && statisticsResult.Success,
            RecordsAffected = totalRecordsAffected
        };
    }
}
