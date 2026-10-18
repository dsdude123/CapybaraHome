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
    Backups__Destination=/data/backups

RUN mkdir -p /data && chown app:app /data
VOLUME /data
USER app
EXPOSE 8080

COPY --from=build /app ./

HEALTHCHECK --interval=30s --timeout=3s --start-period=20s \
    CMD ["/bin/sh", "-c", "wget -qO- http://127.0.0.1:8080/health >/dev/null || exit 1"]

ENTRYPOINT ["dotnet", "CapybaraHome.Web.dll"]
