# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy solution and project files for caching package restores
COPY POS-API.slnx ./
COPY src/POS.Domain/POS.Domain.csproj src/POS.Domain/
COPY src/POS.Application/POS.Application.csproj src/POS.Application/
COPY src/POS.Infrastructure/POS.Infrastructure.csproj src/POS.Infrastructure/
COPY src/POS.Api/POS.Api.csproj src/POS.Api/
COPY tests/POS.UnitTests/POS.UnitTests.csproj tests/POS.UnitTests/
COPY tests/POS.ApplicationTests/POS.ApplicationTests.csproj tests/POS.ApplicationTests/
COPY tests/POS.IntegrationTests/POS.IntegrationTests.csproj tests/POS.IntegrationTests/

# Restore dependencies
RUN dotnet restore POS-API.slnx

# Copy source code and build
COPY src/ src/
COPY tests/ tests/

RUN dotnet build POS-API.slnx -c Release --no-restore
RUN dotnet test POS-API.slnx -c Release --no-build --no-restore

# Publish the Web API
RUN dotnet publish src/POS.Api/POS.Api.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for security
USER $APP_UID

# Copy compiled binaries from publish stage
COPY --from=build /app/publish .

# Environment configuration
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "POS.Api.dll"]
