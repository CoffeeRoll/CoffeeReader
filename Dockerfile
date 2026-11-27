# syntax=docker/dockerfile:1

# ---- Web app ------------------------------------------------------------------------------------------
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---- API ----------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props CoffeeReader.slnx ./
COPY src/CoffeeReader.Domain/CoffeeReader.Domain.csproj src/CoffeeReader.Domain/
COPY src/CoffeeReader.Application/CoffeeReader.Application.csproj src/CoffeeReader.Application/
COPY src/CoffeeReader.Infrastructure/CoffeeReader.Infrastructure.csproj src/CoffeeReader.Infrastructure/
COPY src/CoffeeReader.Api/CoffeeReader.Api.csproj src/CoffeeReader.Api/
RUN dotnet restore src/CoffeeReader.Api/CoffeeReader.Api.csproj
COPY src/ src/
RUN dotnet publish src/CoffeeReader.Api/CoffeeReader.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# ---- Runtime ------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
LABEL org.opencontainers.image.title="CoffeeReader" \
      org.opencontainers.image.description="A self-hosted ebook library and reader focused on readability." \
      org.opencontainers.image.licenses="Apache-2.0"

WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__LibraryPath=/books \
    Storage__DataPath=/config \
    HOME=/tmp \
    DOTNET_NOLOGO=1

# /books holds your ebooks; /config holds the database, cover thumbnails and sign-in keys.
RUN mkdir -p /books /config && chown -R app:app /books /config
VOLUME ["/books", "/config"]
EXPOSE 8080

# The image's built-in non-root user; compose can override it to match your files' owner.
USER app
ENTRYPOINT ["dotnet", "CoffeeReader.Api.dll"]
