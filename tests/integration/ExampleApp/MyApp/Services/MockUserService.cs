using Microsoft.Extensions.Logging;
using MyApp.Models;

namespace MyApp.Services;

/// <summary>
/// Mock implementation of IUserService for testing and development.
/// This implementation provides predictable test data without external dependencies.
/// </summary>
public class MockUserService : IUserService
{
    private readonly List<User> _users;
    private readonly ILogger<MockUserService> _logger;
    private int _nextId;

    public MockUserService(ILogger<MockUserService> logger)
    {
        _logger = logger;
        _nextId = 100; // Start with high ID to avoid conflicts
        
        _users = new List<User>
        {
            new User
            {
                Id = 1,
                Name = "Mock User 1",
                Email = "mock1@example.com",
                Bio = "This is a mock user for testing",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                IsActive = true
            },
            new User
            {
                Id = 2,
                Name = "Mock User 2",
                Email = "mock2@example.com",
                Bio = "Another mock user for testing",
                CreatedAt = DateTime.UtcNow.AddDays(-15),
                IsActive = true
            },
            new User
            {
                Id = 3,
                Name = "Inactive Mock User",
                Email = "inactive@example.com",
                Bio = "An inactive mock user",
                CreatedAt = DateTime.UtcNow.AddDays(-60),
                IsActive = false
            }
        };
    }

    public Task<User?> GetUserByIdAsync(int userId)
    {
        _logger.LogInformation("Mock: Fetching user with ID: {UserId}", userId);
        var user = _users.FirstOrDefault(u => u.Id == userId);
        return Task.FromResult(user);
    }

    public Task<User?> GetUserByEmailAsync(string email)
    {
        _logger.LogInformation("Mock: Fetching user with email: {Email}", email);
        var user = _users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(user);
    }

    public Task<IEnumerable<User>> GetAllUsersAsync()
    {
        _logger.LogInformation("Mock: Fetching all {Count} users", _users.Count);
        return Task.FromResult<IEnumerable<User>>(_users.OrderBy(u => u.Name).ToList());
    }

    public Task<User> CreateUserAsync(User user)
    {
        _logger.LogInformation("Mock: Creating new user: {Email}", user.Email);
        
        // Simulate validation
        if (_users.Any(u => u.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"User with email {user.Email} already exists");
        }

        user.Id = _nextId++;
        user.CreatedAt = DateTime.UtcNow;
        _users.Add(user);
        
        return Task.FromResult(user);
    }

    public Task<User> UpdateUserAsync(User user)
    {
        _logger.LogInformation("Mock: Updating user with ID: {UserId}", user.Id);
        
        var existingUser = _users.FirstOrDefault(u => u.Id == user.Id);
        if (existingUser == null)
        {
            throw new InvalidOperationException($"User with ID {user.Id} not found");
        }

        // Update properties
        existingUser.Name = user.Name;
        existingUser.Email = user.Email;
        existingUser.Bio = user.Bio;
        existingUser.IsActive = user.IsActive;
        existingUser.UpdatedAt = DateTime.UtcNow;

        return Task.FromResult(existingUser);
    }

    public Task<bool> DeleteUserAsync(int userId)
    {
        _logger.LogInformation("Mock: Deleting user with ID: {UserId}", userId);
        
        var user = _users.FirstOrDefault(u => u.Id == userId);
        if (user == null) return Task.FromResult(false);

        _users.Remove(user);
        return Task.FromResult(true);
    }

    public Task<bool> IsUserActiveAsync(int userId)
    {
        var user = _users.FirstOrDefault(u => u.Id == userId);
        return Task.FromResult(user?.IsActive ?? false);
    }

    public Task<int> GetTotalUserCountAsync()
    {
        _logger.LogInformation("Mock: Getting total user count: {Count}", _users.Count);
        return Task.FromResult(_users.Count);
    }
}
