# FLACie Web. Build from ipodsync/src:  docker build -f FLACie.Server/Dockerfile -t flacie-web .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY FLACie.Core/FLACie.Core.csproj FLACie.Core/
COPY FLACie.Server/FLACie.Server.csproj FLACie.Server/
RUN dotnet restore FLACie.Server/FLACie.Server.csproj
COPY FLACie.Core/ FLACie.Core/
COPY FLACie.Server/ FLACie.Server/
RUN dotnet publish FLACie.Server/FLACie.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
LABEL org.opencontainers.image.licenses="MIT" \n      org.opencontainers.image.source="https://github.com/asch-bh08/FLACie" \n      org.opencontainers.image.description="FLACie Web: a self-hosted music player for Jellyfin or a NAS"
WORKDIR /app
COPY --from=build /app .
# sign-in keys and the cover cache live in /data: mount a volume there so restarts keep everyone signed in
RUN mkdir -p /data
ENV ASPNETCORE_URLS=http://+:8080 \
    FLACIE_DATA=/data
VOLUME /data
EXPOSE 8080

COPY FLACie.Server/entrypoint.sh /entrypoint.sh
RUN sed -i 's/$//' /entrypoint.sh && chmod +x /entrypoint.sh
# starts as root only to fix /data ownership, then runs the app as an unprivileged user (see entrypoint.sh)
ENTRYPOINT ["/entrypoint.sh"]
