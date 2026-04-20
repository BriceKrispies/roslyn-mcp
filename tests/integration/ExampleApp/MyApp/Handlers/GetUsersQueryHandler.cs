using DependencyApp;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyApp.Data;
using MyApp.Messages;
using MyApp.ViewModels;

namespace MyApp.Handlers;

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, UsersViewModel>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetUsersQueryHandler> _logger;
    private readonly IMediator _mediator;

    public GetUsersQueryHandler(ApplicationDbContext context, ILogger<GetUsersQueryHandler> logger, IMediator mediator)
    {
        _context = context;
        _logger = logger;
        _mediator = mediator;
    }

    public async Task<UsersViewModel> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting GetUsersQuery processing...");

        // Step 1: Check if user is authenticated
        var currentUserId = "current_user"; // In real app, this would come from context/claims
        var isAuthenticated = await _mediator.Send(new IsUserAuthenticatedQuery(currentUserId), cancellationToken);
        
        if (!isAuthenticated)
        {
            _logger.LogWarning("User not authenticated, returning empty result");
            return new UsersViewModel
            {
                Users = new List<UserDisplayModel>(),
                PageTitle = "Access Denied",
                Message = "Please log in to view users",
                TotalUsers = 0,
                LastUpdated = DateTime.UtcNow
            };
        }

        // Step 2: Validate user permissions
        var hasReadPermission = await _mediator.Send(new ValidateUserPermissionsQuery(currentUserId, "read_users"), cancellationToken);
        
        if (!hasReadPermission)
        {
            _logger.LogWarning("User {UserId} does not have permission to read users", currentUserId);
            await _mediator.Send(new LogUserActivityCommand(currentUserId, "ACCESS_DENIED", "Attempted to access user list without permission"), cancellationToken);
            
            return new UsersViewModel
            {
                Users = new List<UserDisplayModel>(),
                PageTitle = "Insufficient Permissions",
                Message = "You don't have permission to view users",
                TotalUsers = 0,
                LastUpdated = DateTime.UtcNow
            };
        }

        // Step 3: Get user preferences for pagination/display
        var userPreferences = await _mediator.Send(new GetUserPreferencesQuery(currentUserId), cancellationToken);
        _logger.LogInformation("User preferences loaded: PageSize={PageSize}, Theme={Theme}", 
            userPreferences.PageSize, userPreferences.Theme);

        // Step 4: Log the activity
        await _mediator.Send(new LogUserActivityCommand(currentUserId, "VIEW_USERS", 
            $"Accessing user list with page size: {userPreferences.PageSize}"), cancellationToken);

        _logger.LogInformation("Fetching users from database...");

        // Step 5: Query the database for users (applying user preferences)
        var usersQuery = _context.Users.OrderBy(u => u.Name);
        
        // Apply pagination based on user preferences
        var users = await usersQuery
            .Take(userPreferences.PageSize * 10) // Show multiple pages worth for demo
            .ToListAsync(cancellationToken);

        // Use the DependencyApp library to generate a greeting message
        var greeting = MessageUtilities.FormatGreeting("Web Application");
        var timestamp = MessageUtilities.GetTimestamp();

        // Map to view models
        var userDisplayModels = users.Select(u => new UserDisplayModel
        {
            Id = u.Id,
            Name = u.Name,
            Email = u.Email,
            Bio = u.Bio,
            CreatedAt = u.CreatedAt,
            UpdatedAt = u.UpdatedAt,
            IsActive = u.IsActive
        }).ToList();

        _logger.LogInformation("Retrieved {UserCount} users from database for user {UserId}", users.Count, currentUserId);

        // Log successful completion
        await _mediator.Send(new LogUserActivityCommand(currentUserId, "VIEW_USERS_SUCCESS", 
            $"Successfully retrieved {users.Count} users"), cancellationToken);

        return new UsersViewModel
        {
            Users = userDisplayModels,
            PageTitle = "User Management - MyApp",
            Message = $"{greeting} | {timestamp} | Theme: {userPreferences.Theme} | Page Size: {userPreferences.PageSize}",
            TotalUsers = users.Count,
            LastUpdated = DateTime.UtcNow
        };
    }
}
