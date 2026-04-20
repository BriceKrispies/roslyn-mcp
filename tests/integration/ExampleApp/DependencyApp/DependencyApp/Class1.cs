namespace DependencyApp;

public static class MessageUtilities
{
    public static string FormatGreeting(string name)
    {
        return $"Hello from DependencyApp library! Welcome, {name}!";
    }
    
    public static string GetTimestamp()
    {
        return $"Message generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
    }
}
