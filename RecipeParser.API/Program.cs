using System.Net;
using System.Reflection;
using Jering.Javascript.NodeJS;
using Microsoft.OpenApi.Models;
using RecipeParser.Application.Services;
using RecipeParser.Domain.Interfaces;
using RecipeParser.Infrastructure;
using HttpVersion = System.Net.HttpVersion;

var builder = WebApplication.CreateBuilder(args);

var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
builder.Configuration
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{environmentName}.json", false)
    .AddEnvironmentVariables();

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly());
}

builder.Services.AddControllers();

builder.Services.AddTransient<IRecipeParserService, RecipeParserService>();
builder.Services.AddRecipeParserInfrastructure(builder.Configuration);

builder.Services.AddHttpClient("recipe-fetcher", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestVersion = HttpVersion.Version30;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    
    client.DefaultRequestHeaders.UserAgent.ParseAdd("RecipeParser/1.0 (+https://tomk.online) Mozilla/5.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
    client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, br, deflate");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-GB,en;q=0.8");
}) .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    AllowAutoRedirect = true,
    UseCookies = true,
    MaxConnectionsPerServer = 8
});

builder.Services.Configure<NodeJSProcessOptions>(opts =>
{
    opts.ProjectPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Node");
});
builder.Services.AddNodeJS();
builder.Services.AddEndpointsApiExplorer();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Version = "v1",
            Title = "Sporkast API",
            Description = "Sporkast's Recipe Parsing API",
            Contact = new OpenApiContact
            {
                Email = "sporkast-dev@tomk.online",
                Name = "Sporkast Developer",
            }
        });
        var filePath = Path.Combine(AppContext.BaseDirectory, "RecipeParser.API.xml");
        c.IncludeXmlComments(filePath);
    });
}

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();
app.Run();
