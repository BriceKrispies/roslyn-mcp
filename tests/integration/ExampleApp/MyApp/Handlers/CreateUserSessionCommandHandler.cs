using MediatR;
using Microsoft.Extensions.Logging;
using MyApp.Data;
using MyApp.Messages;
using MyApp.Models;

namespace MyApp.Handlers;

public class CreateUserSessionCommandHandler : IRequestHandler<CreateUserSessionCommand, string>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CreateUserSessionCommandHandler> _logger;

    public CreateUserSessionCommandHandler(ApplicationDbContext context, ILogger<CreateUserSessionCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<string> Handle(CreateUserSessionCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating session for user: {UserId}", request.UserId);

        try
        {
            // Generate unique session token
            var sessionToken = $"sess_{Guid.NewGuid():N}_{DateTime.UtcNow.Ticks}";
            
            // Determine expiration
            var expiration = request.CustomExpiration.HasValue 
                ? DateTime.UtcNow.Add(request.CustomExpiration.Value)
                : DateTime.UtcNow.AddHours(8); // Default 8 hours

            // Create session entity
            var session = new UserSession
            {
                UserId = request.UserId,
                SessionToken = sessionToken,
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                ExpiresAt = expiration,
                IsActive = true,
                IpAddress = request.IpAddress,
                UserAgent = request.UserAgent,
                AccessCount = 1
            };

            // CRITICAL: THIS IS A DATABASE WRITE OPERATION - ADD SESSION
            _context.UserSessions.Add(session);
            await _context.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Successfully created session {SessionToken} for user {UserId}, expires at {ExpiresAt}", 
                sessionToken, request.UserId, expiration);

            // Optional: Clean up old expired sessions for this user (another DB write)
            await CleanupExpiredSessionsForUser(request.UserId, cancellationToken);

            return sessionToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session for user: {UserId}", request.UserId);
            throw;
        }
    }

    private async Task CleanupExpiredSessionsForUser(string userId, CancellationToken cancellationToken)
    {
        try
        {
            // ANOTHER DATABASE OPERATION - BULK DELETE EXPIRED SESSIONS
            var expiredSessions = _context.UserSessions
                .Where(s => s.UserId == userId && 
                           (s.ExpiresAt < DateTime.UtcNow || !s.IsActive))
                .ToList();

            if (expiredSessions.Any())
            {
                _context.UserSessions.RemoveRange(expiredSessions);
                await _context.SaveChangesAsync(cancellationToken);
                
                _logger.LogDebug("Cleaned up {Count} expired sessions for user {UserId}", 
                    expiredSessions.Count, userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup expired sessions for user: {UserId}", userId);
            // Don't throw - session creation was successful
        }
    }
}
