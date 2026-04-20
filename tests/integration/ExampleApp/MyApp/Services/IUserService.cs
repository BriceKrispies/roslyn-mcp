using MyApp.Models;

namespace MyApp.Services;

/// <summary>
/// Service interface for user management operations.
/// Multiple implementations test LSP's ability to find interface implementations.
/// </summary>
public interface IUserService
{
    Task<User?> GetUserByIdAsync(int userId);
    Task<User?> GetUserByEmailAsync(string email);
    Task<IEnumerable<User>> GetAllUsersAsync();
    Task<User> CreateUserAsync(User user);
    Task<User> UpdateUserAsync(User user);
    Task<bool> DeleteUserAsync(int userId);
    Task<bool> IsUserActiveAsync(int userId);
    Task<int> GetTotalUserCountAsync();
}
