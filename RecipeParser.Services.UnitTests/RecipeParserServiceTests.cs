using NUnit.Framework;
using RecipeParser.Application.Services;
using RecipeParser.Domain.Interfaces;

namespace RecipeParser.Services.UnitTests;

[TestFixture]
public partial class RecipeParserServiceTests
{
    private IRecipeParserService _sut;
    
    [SetUp]
    public void Setup()
    {
        _sut = new RecipeParserService();
    }
}