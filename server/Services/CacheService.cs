using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace mcp_server.Services;

public class CacheService : ICacheService, IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CacheService> _logger;
    private readonly ConcurrentDictionary<string, object> _persistentCache = new();
    private readonly string _cacheDirectory;
    private readonly string _cacheFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private bool _isDisposed = false;

    public CacheService(IMemoryCache memoryCache, ILogger<CacheService> logger)
    {
        _memoryCache = memoryCache;
        _logger = logger;
        
        _cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dotnet-lsp-mcp");
        _cacheFilePath = Path.Combine(_cacheDirectory, "cache.json");
        
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        await Task.CompletedTask;
        
        // First try memory cache
        if (_memoryCache.TryGetValue(key, out var memoryValue) && memoryValue is T memoryResult)
        {
            _logger.LogDebug("Cache hit (memory): {Key}", key);
            return memoryResult;
        }

        // Then try persistent cache
        if (_persistentCache.TryGetValue(key, out var persistentValue) && persistentValue is T persistentResult)
        {
            _logger.LogDebug("Cache hit (persistent): {Key}", key);
            
            // Move to memory cache for faster access
            _memoryCache.Set(key, persistentResult, TimeSpan.FromMinutes(30));
            return persistentResult;
        }

        _logger.LogDebug("Cache miss: {Key}", key);
        return null;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        await Task.CompletedTask;
        
        var cacheExpiration = expiration ?? TimeSpan.FromHours(1);
        
        // Set in memory cache
        _memoryCache.Set(key, value, cacheExpiration);
        
        // Set in persistent cache
        _persistentCache.AddOrUpdate(key, value, (_, _) => value);
        
        _logger.LogDebug("Cache set: {Key} (expires in {Expiration})", key, cacheExpiration);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        _memoryCache.Remove(key);
        _persistentCache.TryRemove(key, out _);
        
        _logger.LogDebug("Cache remove: {Key}", key);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        if (_memoryCache is MemoryCache mc)
        {
            mc.Clear();
        }
        
        _persistentCache.Clear();
        
        _logger.LogInformation("Cache cleared");
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        return _memoryCache.TryGetValue(key, out _) || _persistentCache.ContainsKey(key);
    }

    public async Task SaveToDiskAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var serializedCache = new Dictionary<string, object>();
            
            foreach (var kvp in _persistentCache)
            {
                // Only save serializable objects
                try
                {
                    var serialized = JsonConvert.SerializeObject(kvp.Value);
                    var metadata = new CacheMetadata
                    {
                        Type = kvp.Value.GetType().AssemblyQualifiedName!,
                        Data = serialized,
                        Timestamp = DateTime.UtcNow
                    };
                    serializedCache[kvp.Key] = metadata;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to serialize cache item: {Key}", kvp.Key);
                }
            }

            var json = JsonConvert.SerializeObject(serializedCache, Formatting.Indented);
            await File.WriteAllTextAsync(_cacheFilePath, json, cancellationToken);
            
            _logger.LogDebug("Cache saved to disk: {CacheFilePath} ({ItemCount} items)", _cacheFilePath, serializedCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cache to disk");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task LoadFromDiskAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_cacheFilePath))
        {
            _logger.LogDebug("No cache file found: {CacheFilePath}", _cacheFilePath);
            return;
        }

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var json = await File.ReadAllTextAsync(_cacheFilePath, cancellationToken);
            var serializedCache = JsonConvert.DeserializeObject<Dictionary<string, CacheMetadata>>(json);
            
            if (serializedCache == null)
            {
                _logger.LogWarning("Failed to deserialize cache file");
                return;
            }

            foreach (var kvp in serializedCache)
            {
                try
                {
                    // Check if cache item is still valid (not older than 24 hours)
                    if (DateTime.UtcNow - kvp.Value.Timestamp > TimeSpan.FromHours(24))
                    {
                        _logger.LogDebug("Skipping expired cache item: {Key}", kvp.Key);
                        continue;
                    }

                    var type = Type.GetType(kvp.Value.Type);
                    if (type != null)
                    {
                        var value = JsonConvert.DeserializeObject(kvp.Value.Data, type);
                        if (value != null)
                        {
                            _persistentCache.TryAdd(kvp.Key, value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize cache item: {Key}", kvp.Key);
                }
            }
            
            _logger.LogDebug("Cache loaded from disk: {ItemCount} items", _persistentCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cache from disk");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            // Save cache before disposing
            try
            {
                SaveToDiskAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save cache during disposal");
            }
            
            _fileLock?.Dispose();
            _isDisposed = true;
        }
    }

    private class CacheMetadata
    {
        public string Type { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
