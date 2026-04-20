using MediatR;

namespace MyApp.Messages;

public record PerformMaintenanceCommand(
    string UserId,
    string MaintenanceType,
    bool ForceExecution = false) : IRequest<MaintenanceResult>;

public class MaintenanceResult
{
    public bool Success { get; set; }
    public int RecordsAffected { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
}
