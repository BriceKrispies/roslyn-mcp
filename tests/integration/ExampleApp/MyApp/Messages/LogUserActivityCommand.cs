using MediatR;

namespace MyApp.Messages;

public record LogUserActivityCommand(string UserId, string Activity, string? Details = null) : IRequest;
