# Build the app, then run it on the runtime image with a volume for the database.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CapybaraHome.slnx ./
COPY src/CapybaraHome.Domain/CapybaraHome.Domain.csproj src/CapybaraHome.Domain/
COPY src/CapybaraHome.Application/CapybaraHome.Application.csproj src/CapybaraHome.Application/
COPY src/CapybaraHome.Infrastructure/CapybaraHome.Infrastructure.csproj src/CapybaraHome.Infrastructure/
COPY src/CapybaraHome.Web/CapybaraHome.Web.csproj src/CapybaraHome.Web/
COPY tests/CapybaraHome.Tests/CapybaraHome.Tests.csproj tests/CapybaraHome.Tests/
RUN dotnet restore src/CapybaraHome.Web/CapybaraHome.Web.csproj

COPY . ./
RUN dotnet publish src/CapybaraHome.Web/CapybaraHome.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__CapybaraHome="Data Source=/data/capybarahome.db" \
    Backups__Destination=/data/backups \
    DataProtection__KeyPath=/data/keys

RUN mkdir -p /data && chown app:app /data
VOLUME /data
USER app
EXPOSE 8080

COPY --from=build /app ./

# The runtime image carries no HTTP client — no wget, no curl — so the check
# opens the socket with bash's /dev/tcp and matches the body of /health. That
# also makes it a real check rather than a liveness ping: a process that is
# listening but not serving fails it. /bin/sh is dash here, which has no
# /dev/tcp, so bash is named outright. The start period covers the EF Core
# migrations and the dashboard seed, which both run before the port opens.
HEALTHCHECK --interval=30s --timeout=10s --start-period=120s \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && grep -q '\"status\":\"ok\"' <&3"]

ENTRYPOINT ["dotnet", "CapybaraHome.Web.dll"]
