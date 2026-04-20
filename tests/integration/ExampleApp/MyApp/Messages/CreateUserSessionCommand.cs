using MediatR;

namespace MyApp.Messages;

public record CreateUserSessionCommand(
    string UserId, 
    string? IpAddress = null, 
    string? UserAgent = null,
    TimeSpan? CustomExpiration = null) : IRequest<string>; // Returns session token
