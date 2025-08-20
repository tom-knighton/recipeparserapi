# Use the official .NET 9.0 SDK image for build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY RecipeParser.API/*.csproj ./RecipeParser.API/
RUN dotnet restore ./RecipeParser.API/RecipeParser.API.csproj

# Copy the rest of the source code
COPY . .

# Build and publish the app
RUN dotnet publish ./RecipeParser.API/RecipeParser.API.csproj -c Release -o /out

# Use the official .NET 9.0 runtime image for the final container
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out ./

# Expose port 80
EXPOSE 80

# Set the entrypoint
ENTRYPOINT ["dotnet", "RecipeParser.API.dll"]
