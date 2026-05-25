using Microsoft.AspNetCore.Mvc;
using RecipeParser.Domain.Discovery;

namespace RecipeParser.Controllers;

/// <summary>
/// Provides anonymous recipe discovery feed and feedback endpoints.
/// </summary>
/// <param name="discovery">The discovery orchestration service.</param>
[ApiController]
[Route("[controller]")]
public sealed class DiscoveryController(IDiscoveryFeedService discovery) : ControllerBase
{
    /// <summary>
    /// Returns a ranked recipe discovery feed for an anonymous installation/home context.
    /// </summary>
    [HttpPost("Feed")]
    [ProducesResponseType<DiscoveryFeedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Feed([FromBody] DiscoveryFeedRequest request, CancellationToken ct)
    {
        var response = await discovery.GetFeed(request, ct);
        return Ok(response);
    }

    /// <summary>
    /// Registers recipe source signals from the client without returning a feed.
    /// </summary>
    [HttpPost("RegisterSources")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterSources([FromBody] DiscoveryRegisterSourcesRequest request, CancellationToken ct)
    {
        await discovery.RegisterSources(request, ct);
        return NoContent();
    }

    /// <summary>
    /// Records recipe discovery feedback events such as open, hide, or import success.
    /// </summary>
    [HttpPost("Feedback")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Feedback([FromBody] DiscoveryFeedbackRequest request, CancellationToken ct)
    {
        try
        {
            await discovery.RecordFeedback(request, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid discovery feedback",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }
}
