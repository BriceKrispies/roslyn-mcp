using System.ComponentModel.DataAnnotations;

namespace MyApp.Models;

public class UserSession
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(200)]
    public string SessionToken { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? LastAccessedAt { get; set; }
    
    public DateTime ExpiresAt { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    [MaxLength(50)]
    public string? IpAddress { get; set; }
    
    [MaxLength(500)]
    public string? UserAgent { get; set; }
    
    public int AccessCount { get; set; } = 1;
}
