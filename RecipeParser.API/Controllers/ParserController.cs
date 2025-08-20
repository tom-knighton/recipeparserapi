using Microsoft.AspNetCore.Mvc;

namespace RecipeParser.Controllers;

[ApiController]
[Route("[controller]")]
public class ParserController: ControllerBase
{
    [HttpPost("Parse")]
    public async Task<IActionResult> Parse()
    {
        return Ok();
    }
}