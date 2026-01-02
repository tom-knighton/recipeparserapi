# Use the official .NET 9.0 SDK image for build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY RecipeParser.API/*.csproj ./RecipeParser.API/
RUN dotnet restore ./RecipeParser.API/RecipeParser.API.csproj

# --- Node install & npm ci ---
ARG NODE_DIR=RecipeParser.Application/Services/Node
COPY ${NODE_DIR}/package*.json /tmp/node/
RUN apt-get update \
 && apt-get install -y --no-install-recommends nodejs npm ca-certificates \
 && if [ ! -e /usr/bin/node ] && [ -e /usr/bin/nodejs ]; then ln -s /usr/bin/nodejs /usr/bin/node; fi \
 && npm ci --prefix /tmp/node --omit=dev \
 && rm -rf /var/lib/apt/lists/*
# --- end Node prep ---

# Copy the rest of the source code
COPY . .

# Build and publish the app
RUN dotnet publish ./RecipeParser.API/RecipeParser.API.csproj -c Release -o /out

# Copy Node.js files to the output directory
RUN mkdir -p /out/Node \
 && cp -a /tmp/node/node_modules /out/Node/ \
 && cp -a ${NODE_DIR}/*.js /out/Node/ 2>/dev/null || true \
 && cp -a ${NODE_DIR}/*.cjs /out/Node/ 2>/dev/null || true

# Use the official .NET 9.0 runtime image for the final container
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Install Node
ENV NODE_ENV=production
RUN apt-get update \
 && apt-get install -y --no-install-recommends nodejs ca-certificates \
 && if [ ! -e /usr/bin/node ] && [ -e /usr/bin/nodejs ]; then ln -s /usr/bin/nodejs /usr/bin/node; fi \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /out ./

# Expose port 8080
ENV ASPNETCORE_URLS=http://*:8080
EXPOSE 8080

# Set the entrypoint
ENTRYPOINT ["dotnet", "RecipeParser.API.dll"]