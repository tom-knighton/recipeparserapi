using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeParser.Infrastructure.Discovery.Migrations
{
    /// <inheritdoc />
    public partial class InitialDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sporkast");

            migrationBuilder.CreateTable(
                name: "DiscoveryCandidates",
                schema: "sporkast",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    NormalizedSourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SourceDomain = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TotalMinutes = table.Column<double>(type: "double precision", nullable: true),
                    Rating = table.Column<double>(type: "double precision", nullable: true),
                    RatingCount = table.Column<int>(type: "integer", nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FreshnessScore = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryCandidates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryFeedbackEvents",
                schema: "sporkast",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    NormalizedSourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryFeedbackEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryFeedCaches",
                schema: "sporkast",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CacheKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryFeedCaches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryProfiles",
                schema: "sporkast",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    HomeId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Locale = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscoverySourceAffinities",
                schema: "sporkast",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDomain = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    SeenCount = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoverySourceAffinities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryCandidates_NormalizedSourceUrl",
                table: "DiscoveryCandidates",
                schema: "sporkast",
                column: "NormalizedSourceUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryCandidates_SourceDomain",
                table: "DiscoveryCandidates",
                schema: "sporkast",
                column: "SourceDomain");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryFeedbackEvents_CreatedAt",
                table: "DiscoveryFeedbackEvents",
                schema: "sporkast",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryFeedbackEvents_ProfileId_NormalizedSourceUrl",
                table: "DiscoveryFeedbackEvents",
                schema: "sporkast",
                columns: new[] { "ProfileId", "NormalizedSourceUrl" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryFeedCaches_ExpiresAt",
                table: "DiscoveryFeedCaches",
                schema: "sporkast",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryFeedCaches_ProfileId_CacheKey",
                table: "DiscoveryFeedCaches",
                schema: "sporkast",
                columns: new[] { "ProfileId", "CacheKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryProfiles_InstallationId_HomeId",
                table: "DiscoveryProfiles",
                schema: "sporkast",
                columns: new[] { "InstallationId", "HomeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoverySourceAffinities_ProfileId_SourceDomain",
                table: "DiscoverySourceAffinities",
                schema: "sporkast",
                columns: new[] { "ProfileId", "SourceDomain" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoveryCandidates",
                schema: "sporkast");

            migrationBuilder.DropTable(
                name: "DiscoveryFeedbackEvents",
                schema: "sporkast");

            migrationBuilder.DropTable(
                name: "DiscoveryFeedCaches",
                schema: "sporkast");

            migrationBuilder.DropTable(
                name: "DiscoveryProfiles",
                schema: "sporkast");

            migrationBuilder.DropTable(
                name: "DiscoverySourceAffinities",
                schema: "sporkast");
        }
    }
}
