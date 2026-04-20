using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MyApp.Models;

namespace MyApp.Services;

/// <summary>
/// In-memory cached implementation of IUserService.
/// This implementation provides fast access to user data through memory caching.
/// </summary>
public class CacheUserService : IUserService
{
    private readonly IUserService _baseUserService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheUserService> _logger;
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(30);

    public CacheUserService(IUserService baseUserService, IMemoryCache cache, ILogger<CacheUserService> logger)
    {
        _baseUserService = baseUserService;
        _cache = cache;
        _logger = logger;
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        var cacheKey = $"user_id_{userId}";
        if (_cache.TryGetValue(cacheKey, out User? cachedUser))
        {
            _logger.LogInformation("Cache HIT for user ID: {UserId}", userId);
            return cachedUser;
        }

        _logger.LogInformation("Cache MISS for user ID: {UserId}, fetching from base service", userId);
        var user = await _baseUserService.GetUserByIdAsync(userId);
        if (user != null)
        {
            _cache.Set(cacheKey, user, CacheExpiry);
        }
        return user;
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        var cacheKey = $"user_email_{email}";
        if (_cache.TryGetValue(cacheKey, out User? cachedUser))
        {
            _logger.LogInformation("Cache HIT for user email: {Email}", email);
            return cachedUser;
        }

        _logger.LogInformation("Cache MISS for user email: {Email}, fetching from base service", email);
        var user = await _baseUserService.GetUserByEmailAsync(email);
        if (user != null)
        {
            _cache.Set(cacheKey, user, CacheExpiry);
            _cache.Set($"user_id_{user.Id}", user, CacheExpiry); // Also cache by ID
        }
        return user;
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync()
    {
        const string cacheKey = "all_users";
        if (_cache.TryGetValue(cacheKey, out IEnumerable<User>? cachedUsers))
        {
            _logger.LogInformation("Cache HIT for all users");
            return cachedUsers!;
        }

        _logger.LogInformation("Cache MISS for all users, fetching from base service");
        var users = await _baseUserService.GetAllUsersAsync();
        _cache.Set(cacheKey, users, TimeSpan.FromMinutes(10)); // Shorter cache for bulk data
        return users;
    }

    public async Task<User> CreateUserAsync(User user)
    {
        var result = await _baseUserService.CreateUserAsync(user);
        InvalidateUserCaches();
        return result;
    }

    public async Task<User> UpdateUserAsync(User user)
    {
        var result = await _baseUserService.UpdateUserAsync(user);
        InvalidateUserCache(user.Id, user.Email);
        return result;
    }

    public async Task<bool> DeleteUserAsync(int userId)
    {
        var user = await GetUserByIdAsync(userId);
        var result = await _baseUserService.DeleteUserAsync(userId);
        if (result && user != null)
        {
            InvalidateUserCache(userId, user.Email);
        }
        return result;
    }

    public async Task<bool> IsUserActiveAsync(int userId)
    {
        var user = await GetUserByIdAsync(userId);
        return user?.IsActive ?? false;
    }

    public async Task<int> GetTotalUserCountAsync()
    {
        const string cacheKey = "total_user_count";
        if (_cache.TryGetValue(cacheKey, out int cachedCount))
        {
            _logger.LogInformation("Cache HIT for total user count");
            return cachedCount;
        }

        _logger.LogInformation("Cache MISS for total user count, fetching from base service");
        var count = await _baseUserService.GetTotalUserCountAsync();
        _cache.Set(cacheKey, count, TimeSpan.FromMinutes(5)); // Short cache for counts
        return count;
    }

    private void InvalidateUserCache(int userId, string email)
    {
        _cache.Remove($"user_id_{userId}");
        _cache.Remove($"user_email_{email}");
        _cache.Remove("all_users");
        _cache.Remove("total_user_count");
        _logger.LogInformation("Invalidated cache for user ID: {UserId}, Email: {Email}", userId, email);
    }

    private void InvalidateUserCaches()
    {
        _cache.Remove("all_users");
        _cache.Remove("total_user_count");
        _logger.LogInformation("Invalidated bulk user caches");
    }
}
