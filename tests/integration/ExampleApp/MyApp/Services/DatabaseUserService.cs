using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyApp.Data;
using MyApp.Models;

namespace MyApp.Services;

/// <summary>
/// Database-backed implementation of IUserService using Entity Framework Core.
/// This implementation provides persistent storage through the ApplicationDbContext.
/// </summary>
public class DatabaseUserService : IUserService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DatabaseUserService> _logger;

    public DatabaseUserService(ApplicationDbContext context, ILogger<DatabaseUserService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        _logger.LogInformation("Fetching user with ID: {UserId} from database", userId);
        return await _context.Users.FindAsync(userId);
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        _logger.LogInformation("Fetching user with email: {Email} from database", email);
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync()
    {
        _logger.LogInformation("Fetching all users from database");
        return await _context.Users.OrderBy(u => u.Name).ToListAsync();
    }

    public async Task<User> CreateUserAsync(User user)
    {
        _logger.LogInformation("Creating new user: {Email} in database", user.Email);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<User> UpdateUserAsync(User user)
    {
        _logger.LogInformation("Updating user with ID: {UserId} in database", user.Id);
        user.UpdatedAt = DateTime.UtcNow;
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<bool> DeleteUserAsync(int userId)
    {
        _logger.LogInformation("Deleting user with ID: {UserId} from database", userId);
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return false;

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> IsUserActiveAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        return user?.IsActive ?? false;
    }

    public async Task<int> GetTotalUserCountAsync()
    {
        return await _context.Users.CountAsync();
    }
}
