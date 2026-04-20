using MediatR;
using Microsoft.Extensions.Logging;
using MyApp.Messages;

namespace MyApp.Handlers;

public class GetUserPreferencesQueryHandler : IRequestHandler<GetUserPreferencesQuery, UserPreferences>
{
    private readonly ILogger<GetUserPreferencesQueryHandler> _logger;

    public GetUserPreferencesQueryHandler(ILogger<GetUserPreferencesQueryHandler> logger)
    {
        _logger = logger;
    }

    public Task<UserPreferences> Handle(GetUserPreferencesQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching preferences for user: {UserId}", request.UserId);

        // Simulate different preferences based on user ID
        var preferences = new UserPreferences
        {
            Theme = request.UserId.Contains("admin") ? "dark" : "light",
            Language = "en-US",
            PageSize = request.UserId.Contains("admin") ? 50 : 10,
            EmailNotifications = true,
            TimeZone = request.UserId.Contains("eu") ? "Europe/London" : "UTC"
        };

        _logger.LogInformation("Retrieved preferences for user {UserId}: Theme={Theme}, PageSize={PageSize}", 
            request.UserId, preferences.Theme, preferences.PageSize);

        return Task.FromResult(preferences);
    }
}
