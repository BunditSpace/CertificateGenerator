# ============================================================================
# Stage 1: Build
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CertificateGenerator.Api.csproj ./
RUN dotnet restore CertificateGenerator.Api.csproj

COPY . ./
RUN dotnet publish CertificateGenerator.Api.csproj -c Release -o /app/publish --no-restore

# ============================================================================
# Stage 2: Runtime
# ============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install native fontconfig/freetype dependencies often needed by Skia/QuestPDF on Linux
RUN apt-get update && apt-get install -y --no-install-recommends \
    libfontconfig1 \
    libfreetype6 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./
# Ensure Fonts/ directory is explicitly present in the final image
COPY Fonts/ ./Fonts/

# Render & Railway inject PORT; default to 8080 locally
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet CertificateGenerator.Api.dll"]
