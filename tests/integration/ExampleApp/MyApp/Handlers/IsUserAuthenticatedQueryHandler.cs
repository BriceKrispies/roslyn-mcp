using MediatR;
using Microsoft.Extensions.Logging;
using MyApp.Messages;

namespace MyApp.Handlers;

public class IsUserAuthenticatedQueryHandler : IRequestHandler<IsUserAuthenticatedQuery, bool>
{
    private readonly ILogger<IsUserAuthenticatedQueryHandler> _logger;

    public IsUserAuthenticatedQueryHandler(ILogger<IsUserAuthenticatedQueryHandler> logger)
    {
        _logger = logger;
    }

    public Task<bool> Handle(IsUserAuthenticatedQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking authentication for user: {UserId}", request.UserId ?? "anonymous");
        
        // For demo purposes, always return true
        _logger.LogInformation("User authentication check passed");
        return Task.FromResult(true);
    }
}
