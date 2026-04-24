# ============================================================================
# BUILD STAGE: Compile and publish .NET application
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files first (stable layer - benefits from layer caching)
COPY PoshMcp.Server/PoshMcp.csproj ./PoshMcp.Server/

# Restore dependencies in one stage for efficiency
# This layer is cached unless project files change
RUN dotnet restore PoshMcp.Server/PoshMcp.csproj \
    && dotnet nuget locals http-cache --clear

# Copy all source code (changes frequently - placed after stable dependencies)
COPY . .

# Build and publish the PoshMcp.Server application in Release configuration
# Use /p:UseAppHost=false for portability across architectures
RUN dotnet build PoshMcp.Server/PoshMcp.csproj -c Release --no-restore \
    && dotnet publish PoshMcp.Server/PoshMcp.csproj -c Release -o /app/publish/server /p:UseAppHost=false

# Copy startup script to publish output
COPY docker-entrypoint.sh /app/publish/

# ============================================================================
# RUNTIME STAGE: Minimal image with PowerShell and the PoshMcp server
# ============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Set working directory early
WORKDIR /app

# Install system dependencies and PowerShell in a single RUN command
# This reduces layer count and improves image size
# Using apt-get with aggressive cleanup to minimize layer size
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        wget \
        apt-transport-https \
        software-properties-common \
        ca-certificates \
    && wget -q "https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb" \
    && dpkg -i packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        powershell \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/* \
    && rm packages-microsoft-prod.deb \
    && rm -rf /tmp/* /var/tmp/*

# Copy published server application from build stage
COPY --from=build /app/publish/ .

# Bundle the module installer so derived images can use it without copying from the build context
COPY install-modules.ps1 /app/install-modules.ps1

# Make startup script executable
RUN chmod +x docker-entrypoint.sh

# Expose port for HTTP transport mode (8080 standard for non-root containers)
EXPOSE 8080

# Set environment variables for ASP.NET Core and PoshMcp runtime
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    POSHMCP_TRANSPORT=http \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

# Create non-root user (appuser:appuser with UID 1001)
# This improves security by preventing root execution
RUN useradd -m -u 1001 -s /sbin/nologin appuser \
    && chown -R appuser:appuser /app

# Switch to non-root user for runtime
USER appuser

# Health check (Kubernetes-compatible)
# Verifies HTTP transport mode responds with healthy status
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Use startup script as entry point (delegates to poshmcp serve CLI command)
ENTRYPOINT ["./docker-entrypoint.sh"]
