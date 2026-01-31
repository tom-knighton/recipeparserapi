using Jering.Javascript.NodeJS;
using NSubstitute;
using NUnit.Framework;
using RecipeParser.Application.Services;
using RecipeParser.Domain.Interfaces;

namespace RecipeParser.Services.UnitTests;

[TestFixture]
public partial class RecipeParserServiceTests
{
    private INodeJSService _mockNode;
    private IRecipeParserService _sut;
    
    [SetUp]
    public void Setup()
    {
        _mockNode = Substitute.For<INodeJSService>();
        _sut = new RecipeParserService(_mockNode, new PlaywrightPageFetcher(new PlaywrightBrowser()));
    }
}