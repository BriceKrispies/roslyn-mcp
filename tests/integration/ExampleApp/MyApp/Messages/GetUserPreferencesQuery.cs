using MediatR;

namespace MyApp.Messages;

public record GetUserPreferencesQuery(string UserId) : IRequest<UserPreferences>;

public class UserPreferences
{
    public string Theme { get; set; } = "light";
    public string Language { get; set; } = "en-US";
    public int PageSize { get; set; } = 10;
    public bool EmailNotifications { get; set; } = true;
    public string TimeZone { get; set; } = "UTC";
}
