# Use the official .NET 10 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Set the working directory
WORKDIR /src

# Copy solution file and project files
COPY PoshMcp.sln ./
COPY PoshMcp.Web/PoshMcp.Web.csproj ./PoshMcp.Web/
COPY PoshMcp.Server/PoshMcp.csproj ./PoshMcp.Server/

# Restore dependencies for both projects
RUN dotnet restore PoshMcp.Web/PoshMcp.Web.csproj
RUN dotnet restore PoshMcp.Server/PoshMcp.csproj

# Copy the rest of the source code
COPY . .

# Build and publish both applications
WORKDIR /src/PoshMcp.Web
RUN dotnet publish PoshMcp.Web.csproj -c Release -o /app/publish/web /p:UseAppHost=false

WORKDIR /src/PoshMcp.Server
RUN dotnet publish PoshMcp.csproj -c Release -o /app/publish/server /p:UseAppHost=false

# Copy the startup script
COPY docker-entrypoint.sh /app/publish/

# Use the official .NET 10 runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Install PowerShell
RUN apt-get update && apt-get install -y \
    wget \
    apt-transport-https \
    software-properties-common \
    && wget -q "https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb" \
    && dpkg -i packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y powershell \
    && rm -rf /var/lib/apt/lists/* \
    && rm packages-microsoft-prod.deb

# Set the working directory
WORKDIR /app

# Copy both published applications and startup script
COPY --from=build /app/publish/ .

# Make the startup script executable
RUN chmod +x docker-entrypoint.sh

# Expose the port (for web mode)
EXPOSE 8080

# Set default environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV POSHMCP_MODE=web

# Create a non-root user for security
RUN useradd -m -u 1001 appuser && chown -R appuser:appuser /app
USER appuser

# Use the startup script as entry point
ENTRYPOINT ["./docker-entrypoint.sh"]
