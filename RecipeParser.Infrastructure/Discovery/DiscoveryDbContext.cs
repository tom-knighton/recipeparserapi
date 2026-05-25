using Microsoft.EntityFrameworkCore;
using RecipeParser.Domain.Discovery;

namespace RecipeParser.Infrastructure.Discovery;

public sealed class DiscoveryDbContext(DbContextOptions<DiscoveryDbContext> options) : DbContext(options)
{
    public const string SchemaName = "sporkast";

    public DbSet<DiscoveryProfile> DiscoveryProfiles => Set<DiscoveryProfile>();
    public DbSet<DiscoverySourceAffinity> DiscoverySourceAffinities => Set<DiscoverySourceAffinity>();
    public DbSet<DiscoveryCandidate> DiscoveryCandidates => Set<DiscoveryCandidate>();
    public DbSet<DiscoveryFeedbackEvent> DiscoveryFeedbackEvents => Set<DiscoveryFeedbackEvent>();
    public DbSet<DiscoveryFeedCache> DiscoveryFeedCaches => Set<DiscoveryFeedCache>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<DiscoveryProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InstallationId).HasMaxLength(160).IsRequired();
            entity.Property(e => e.HomeId).HasMaxLength(160);
            entity.Property(e => e.Locale).HasMaxLength(24);
            entity.HasIndex(e => new { e.InstallationId, e.HomeId }).IsUnique();
        });

        modelBuilder.Entity<DiscoverySourceAffinity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceDomain).HasMaxLength(240).IsRequired();
            entity.HasIndex(e => new { e.ProfileId, e.SourceDomain }).IsUnique();
        });

        modelBuilder.Entity<DiscoveryCandidate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceUrl).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.NormalizedSourceUrl).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.SourceDomain).HasMaxLength(240).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(512).IsRequired();
            entity.Property(e => e.ImageUrl).HasMaxLength(2048);
            entity.Property(e => e.Tags).HasColumnType("text[]");
            entity.HasIndex(e => e.NormalizedSourceUrl).IsUnique();
            entity.HasIndex(e => e.SourceDomain);
        });

        modelBuilder.Entity<DiscoveryFeedbackEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceUrl).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.NormalizedSourceUrl).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.ProfileId, e.NormalizedSourceUrl });
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<DiscoveryFeedCache>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CacheKey).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ResponseJson).IsRequired();
            entity.HasIndex(e => new { e.ProfileId, e.CacheKey }).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
        });
    }
}
