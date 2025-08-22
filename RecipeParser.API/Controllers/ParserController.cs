using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using RecipeParser.Domain.Interfaces;
using RecipeParser.Domain.Requests;

namespace RecipeParser.Controllers;

[ApiController]
[Route("[controller]")]
public class ParserController(IRecipeParserService parser): ControllerBase
{
    [HttpPost("Parse")]
    public async Task<IActionResult> Parse([Required] [FromBody] ParseUrlRequest request)
    {
        try
        {
            var result = await parser.ParseRecipeByUrl(request.Url);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}