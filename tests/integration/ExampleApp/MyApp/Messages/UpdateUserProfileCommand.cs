using MediatR;

namespace MyApp.Messages;

public record UpdateUserProfileCommand(
    string UserId,
    string? Name = null,
    string? Bio = null,
    bool NotifyUser = true) : IRequest<bool>;
