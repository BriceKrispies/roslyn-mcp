using System.ComponentModel.DataAnnotations;

namespace MyApp.Models;

public class UserActivity
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(200)]
    public string Activity { get; set; } = string.Empty;
    
    [MaxLength(1000)]
    public string? Details { get; set; }
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    [MaxLength(50)]
    public string? IpAddress { get; set; }
    
    [MaxLength(500)]
    public string? UserAgent { get; set; }
    
    public bool IsSuccessful { get; set; } = true;
    
    [MaxLength(500)]
    public string? ErrorMessage { get; set; }
}
