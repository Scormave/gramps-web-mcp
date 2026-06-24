# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src

COPY gramps-web-mcp.sln ./
COPY GrampsWeb.Mcp/GrampsWeb.Mcp.csproj GrampsWeb.Mcp/
RUN dotnet restore GrampsWeb.Mcp/GrampsWeb.Mcp.csproj

COPY GrampsWeb.Mcp/ GrampsWeb.Mcp/
RUN dotnet publish GrampsWeb.Mcp/GrampsWeb.Mcp.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS final
WORKDIR /app

LABEL io.modelcontextprotocol.server.name="io.github.Scormave/gramps-web-mcp"

# --- Gramps Web API (required at runtime; set with -e / secrets / compose) ---
# GRAMPS_API_URL      Base URL of your Gramps Web instance (no trailing slash).
# GRAMPS_USERNAME     API user name.
# GRAMPS_PASSWORD     API password or token.
# GRAMPS_TREE_ID      Tree UUID on that server.
# GRAMPS_READ_ONLY    true|false — when true, block create/update/delete tool calls.

# --- MCP transport (optional; these defaults suit a networked container) ---
# MCP_TRANSPORT       stdio | http | sse — default in image is http for Docker.
# ASPNETCORE_URLS     Listen addresses for http/sse (bind all interfaces in containers).
# MCP_PATH            URL prefix for MCP endpoints (default /mcp).
# MCP_STATELESS       true|false — Streamable HTTP stateless mode (default true for http).
# MCP_ENABLE_LEGACY_SSE  true|false — with http, also expose legacy /sse + /message.
# GET /health         Liveness/readiness probe; 200 when Gramps Web API is reachable.

ENV MCP_TRANSPORT=http \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    MCP_PATH=/mcp

EXPOSE 8080

# Used by Docker HEALTHCHECK and docker-compose health probes.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "GrampsWeb.Mcp.dll"]
