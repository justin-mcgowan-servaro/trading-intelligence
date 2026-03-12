# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY TradingIntelligence.slnx .
COPY TradingIntelligence.Api/TradingIntelligence.Api.csproj TradingIntelligence.Api/
COPY TradingIntelligence.Core/TradingIntelligence.Core.csproj TradingIntelligence.Core/
COPY TradingIntelligence.Infrastructure/TradingIntelligence.Infrastructure.csproj TradingIntelligence.Infrastructure/
COPY TradingIntelligence.Tests/TradingIntelligence.Tests.csproj TradingIntelligence.Tests/

# Restore dependencies
RUN dotnet restore TradingIntelligence.slnx

# Copy all source code
COPY . .

# Build and publish
RUN dotnet publish TradingIntelligence.Api/TradingIntelligence.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install tzdata for SAST timezone + krb5 for Npgsql
RUN apt-get update && \
    apt-get install -y tzdata libgssapi-krb5-2 && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "TradingIntelligence.Api.dll"]