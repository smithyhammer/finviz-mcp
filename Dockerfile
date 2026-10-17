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

# Default is MCP over stdio: run with `docker run -i` so the client can talk on stdin/stdout.
# For HTTP, set MCP_TRANSPORT=http and publish the port, e.g. `-p 127.0.0.1:8000:8000`.
# MCP_HOST only applies in HTTP mode; inside a container the server must listen on all interfaces.
ENV MCP_HOST=0.0.0.0
EXPOSE 8000
ENTRYPOINT ["./FinvizMcpServer"]
