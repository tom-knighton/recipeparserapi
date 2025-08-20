using System.Net;
using System.Reflection;
using AngleSharp;
using AngleSharp.Io;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using RecipeParser.Application.Services;
using RecipeParser.Domain.Interfaces;
using HttpVersion = System.Net.HttpVersion;

var builder = WebApplication.CreateBuilder(args);

var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
builder.Configuration
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{environmentName}.json", false)
    .AddEnvironmentVariables();


builder.Services.AddControllers();

builder.Services.AddTransient<IRecipeParserService, RecipeParserService>();

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

builder.Services.AddEndpointsApiExplorer();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen();
    builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly());
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