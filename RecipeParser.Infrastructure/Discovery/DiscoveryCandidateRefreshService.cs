using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecipeParser.Domain.Discovery;

namespace RecipeParser.Infrastructure.Discovery;

public sealed class DiscoveryCandidateRefreshService(
    IServiceProvider serviceProvider,
    IOptions<DiscoveryOptions> options,
    ILogger<DiscoveryCandidateRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(options.Value.CandidateRefreshIntervalMinutes, 15, 1440));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                using var scope = serviceProvider.CreateScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<IDiscoveryCandidateIngestionService>();
                await ingestion.SyncSourceCandidates(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Discovery scheduled source refresh failed.");
            }
        }
    }
}
