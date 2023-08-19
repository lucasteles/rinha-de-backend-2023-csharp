FROM mcr.microsoft.com/dotnet/sdk:8.0-preview-alpine AS build
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
ENV DOTNET_CLI_TELEMETRY_OPTOUT=true
RUN apk update && apk upgrade
RUN apk add --no-cache clang build-base zlib-dev libssl1.1
COPY ./Pessoas /build
WORKDIR /build

RUN dotnet publish \
    --configuration Release \
    --output /app -r linux-musl-x64

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-preview-alpine-amd64 as final

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DB_HOST=host.docker.internal
ENV CACHE_HOST=host.docker.internal:6379

WORKDIR /app
COPY --from=build /app ./

CMD ["/app/Pessoas"]
