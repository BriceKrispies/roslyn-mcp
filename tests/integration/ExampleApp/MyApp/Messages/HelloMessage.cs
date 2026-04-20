using MediatR;

namespace MyApp.Messages;

public record HelloMessage(string Name) : IRequest;
