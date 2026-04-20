using MediatR;
using Microsoft.Extensions.Logging;
using MyApp.Messages;

namespace MyApp.Handlers;

public class HelloMessageHandler : IRequestHandler<HelloMessage>
{
    private readonly ILogger<HelloMessageHandler> _logger;

    public HelloMessageHandler(ILogger<HelloMessageHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(HelloMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Hello {Name}! Message handled successfully.", request.Name);
        return Task.CompletedTask;
    }
}
