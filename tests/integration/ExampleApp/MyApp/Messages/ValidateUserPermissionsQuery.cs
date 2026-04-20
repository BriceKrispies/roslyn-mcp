using MediatR;

namespace MyApp.Messages;

public record ValidateUserPermissionsQuery(string UserId, string Permission) : IRequest<bool>;
