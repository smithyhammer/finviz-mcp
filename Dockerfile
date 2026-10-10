# syntax=docker/dockerfile:1

# Build on the host's native platform and cross-publish for the target arch (amd64 / arm64).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY FinvizMcpServer.csproj ./
RUN dotnet restore -a $TARGETARCH

COPY . .
RUN dotnet publish -c Release -a $TARGETARCH --no-restore -o /app

# Chiseled runtime-deps: minimal, non-root, includes CA certs; the app is self-contained single-file.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app/FinvizMcpServer ./

# MCP over stdio: run with `docker run -i` so the client can talk on stdin/stdout.
ENTRYPOINT ["./FinvizMcpServer"]
