using MediatR;

namespace MyApp.Messages;

public record IsUserAuthenticatedQuery(string? UserId = null) : IRequest<bool>;
