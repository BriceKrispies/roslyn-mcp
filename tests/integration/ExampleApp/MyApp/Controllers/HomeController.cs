using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MyApp.Messages;
using MyApp.Models;
using MyApp.Services;
using System.Diagnostics;

namespace MyApp.Controllers;

public class HomeController : Controller
{
    private readonly IMediator _mediator;
    private readonly IUserService _userService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IMediator mediator, IUserService userService, ILogger<HomeController> logger)
    {
        _mediator = mediator;
        _userService = userService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        _logger.LogInformation("Home/Index action called");

        try
        {
            // Use MediatR to get the users data
            var viewModel = await _mediator.Send(new GetUsersQuery());
            
            _logger.LogInformation("Successfully retrieved user data for view");
            
            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching users");
            
            // Return a view with error information
            return View("Error");
        }
    }

    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>
    /// Complex endpoint with multiple branching paths through MediatR handlers to database operations.
    /// This endpoint demonstrates call graph tracing from controller → handlers → database.
    /// PERFECT FOR TESTING LSP CALL GRAPH ANALYSIS!
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ProcessUserAction(
        [FromForm] string userId, 
        [FromForm] string actionType, 
        [FromForm] string? data = null, 
        [FromForm] bool isHighPriority = false)
    {
        _logger.LogInformation("🎯 ENDPOINT ENTRY: ProcessUserAction called - UserId: {UserId}, ActionType: {ActionType}, Priority: {IsHighPriority}", 
            userId, actionType, isHighPriority);

        try
        {
            // 🔀 THIS IS THE MAIN BRANCHING POINT - Multiple execution paths through handlers
            var result = await _mediator.Send(new ProcessUserActionCommand(
                userId, 
                actionType, 
                data, 
                isHighPriority));

            if (result.Success)
            {
                // 💾 Success path - Additional logging (saves to database)
                await _mediator.Send(new LogUserActivityCommand(
                    userId,
                    "CONTROLLER_SUCCESS",
                    $"Action {actionType} completed successfully"));

                _logger.LogInformation("✅ SUCCESS: ProcessUserAction completed for user {UserId}", userId);
                
                return Ok(new
                {
                    success = true,
                    message = result.Message,
                    sessionToken = result.SessionToken,
                    activityLogId = result.ActivityLogId,
                    metadata = result.Metadata,
                    timestamp = DateTime.UtcNow
                });
            }
            else
            {
                // ❌ Failure path - Error logging (saves to database)
                await _mediator.Send(new LogUserActivityCommand(
                    userId,
                    "CONTROLLER_FAILURE",
                    $"Action {actionType} failed: {result.Message}"));

                _logger.LogWarning("⚠️ FAILURE: ProcessUserAction failed for user {UserId}: {Message}", userId, result.Message);
                
                return BadRequest(new
                {
                    success = false,
                    message = result.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            // 💥 Exception path - Critical error logging (saves to database)
            _logger.LogError(ex, "💥 EXCEPTION: ProcessUserAction threw exception for user {UserId}", userId);
            
            try
            {
                await _mediator.Send(new LogUserActivityCommand(
                    userId,
                    "CONTROLLER_EXCEPTION",
                    $"Exception in {actionType}: {ex.Message}"));
            }
            catch (Exception logEx)
            {
                _logger.LogCritical(logEx, "🚨 CRITICAL: Failed to log exception for user {UserId}", userId);
            }

            return StatusCode(500, new
            {
                success = false,
                message = "An internal error occurred",
                timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Endpoint that exercises the Autofac decorator chain on IUserService.
    /// Resolved chain: IUserService → CacheUserService (decorator) → DatabaseUserService (concrete, hits EF Core).
    /// Call graph tracing should descend through both implementations and surface the underlying DB ops.
    /// </summary>
    [HttpGet("users/{userId:int}")]
    public async Task<IActionResult> GetUserDetails(int userId)
    {
        _logger.LogInformation("GetUserDetails called for {UserId}", userId);

        var user = await _userService.GetUserByIdAsync(userId);
        if (user is null)
        {
            return NotFound(new { userId, message = "user not found" });
        }

        var isActive = await _userService.IsUserActiveAsync(userId);
        var totalUsers = await _userService.GetTotalUserCountAsync();

        return Ok(new
        {
            user,
            isActive,
            totalUsers,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Endpoint that mutates through the IUserService decorator chain.
    /// Exercises CreateUserAsync / UpdateUserAsync / DeleteUserAsync, which on the
    /// concrete DatabaseUserService translate to EF Core writes.
    /// </summary>
    [HttpPost("users")]
    public async Task<IActionResult> ManageUser([FromBody] User user, [FromQuery] string mode = "create")
    {
        _logger.LogInformation("ManageUser called with mode {Mode}", mode);

        switch (mode)
        {
            case "create":
                var created = await _userService.CreateUserAsync(user);
                return Ok(created);
            case "update":
                var updated = await _userService.UpdateUserAsync(user);
                return Ok(updated);
            case "delete":
                var deleted = await _userService.DeleteUserAsync(user.Id);
                return Ok(new { deleted });
            default:
                return BadRequest(new { message = "unknown mode" });
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
