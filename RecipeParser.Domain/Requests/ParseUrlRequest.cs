using System.ComponentModel.DataAnnotations;

namespace RecipeParser.Domain.Requests;

public class ParseUrlRequest
{
    [Required]
    public string Url { get; set; }
}