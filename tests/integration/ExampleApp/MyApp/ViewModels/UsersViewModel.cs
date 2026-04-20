namespace MyApp.ViewModels;

public class UsersViewModel
{
    public List<UserDisplayModel> Users { get; set; } = new();
    public string PageTitle { get; set; } = "User Management";
    public string Message { get; set; } = string.Empty;
    public int TotalUsers { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class UserDisplayModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; }
    public string StatusText => IsActive ? "Active" : "Inactive";
    public string CreatedAtFormatted => CreatedAt.ToString("yyyy-MM-dd HH:mm");
}
