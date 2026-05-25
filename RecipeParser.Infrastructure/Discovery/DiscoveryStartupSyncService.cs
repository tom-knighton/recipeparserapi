using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecipeParser.Domain.Discovery;

namespace RecipeParser.Infrastructure.Discovery;

public sealed class DiscoveryStartupSyncService(
    IServiceProvider serviceProvider,
    IOptions<DiscoveryOptions> options,
    ILogger<DiscoveryStartupSyncService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.RunCuratedCandidateSyncOnStartup)
            return;

        try
        {
            using var scope = serviceProvider.CreateScope();
            var ingestion = scope.ServiceProvider.GetRequiredService<IDiscoveryCandidateIngestionService>();
            var store = scope.ServiceProvider.GetRequiredService<IDiscoveryStore>();

            await ingestion.SyncCuratedCandidates(cancellationToken);
            await store.DeleteExpiredFeedCaches(cancellationToken);

            var retentionDays = Math.Max(1, options.Value.FeedbackRetentionDays);
            await store.DeleteFeedbackOlderThan(DateTimeOffset.UtcNow.AddDays(-retentionDays), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discovery startup sync failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
