# RecipeParser

## Discovery Database Migrations

Discovery uses EF Core with Postgres through `DiscoveryDbContext`. All discovery tables are created in the `sporkast` schema.

Run commands from the repository root:

```bash
cd /Users/tom/Projects/RecipeParser
```

Set the target database connection string before running migrations:

```bash
export ConnectionStrings__Discovery="Host=localhost;Port=5432;Database=sporkast_discovery;Username=postgres;Password=postgres"
```

Apply pending migrations:

```bash
dotnet ef database update \
  --project RecipeParser.Infrastructure/RecipeParser.Infrastructure.csproj \
  --startup-project RecipeParser.API/RecipeParser.API.csproj \
  --context DiscoveryDbContext
```

Create a new migration after changing discovery entities or mappings:

```bash
dotnet ef migrations add <MigrationName> \
  --project RecipeParser.Infrastructure/RecipeParser.Infrastructure.csproj \
  --startup-project RecipeParser.API/RecipeParser.API.csproj \
  --context DiscoveryDbContext \
  --output-dir Discovery/Migrations
```

List migrations:

```bash
dotnet ef migrations list \
  --project RecipeParser.Infrastructure/RecipeParser.Infrastructure.csproj \
  --startup-project RecipeParser.API/RecipeParser.API.csproj \
  --context DiscoveryDbContext
```

Remove the latest migration before it has been applied anywhere shared:

```bash
dotnet ef migrations remove \
  --project RecipeParser.Infrastructure/RecipeParser.Infrastructure.csproj \
  --startup-project RecipeParser.API/RecipeParser.API.csproj \
  --context DiscoveryDbContext
```

Do not remove or rewrite migrations that have already been applied to a shared or production database. Add a new migration instead.

## Discovery Starter Catalog

`RecipeParser.API/DiscoveryCuratedCandidates.starter.json` contains a paste-ready starter catalog for `Discovery:CuratedCandidates`.

For local testing, copy the `CuratedCandidates` array into `RecipeParser.API/appsettings.Development.json` under `Discovery`.

For deployment, store it in environment-specific app configuration using the same `Discovery:CuratedCandidates` shape. The API syncs configured candidates into the discovery database on startup when `Discovery:RunCuratedCandidateSyncOnStartup` is `true`.
