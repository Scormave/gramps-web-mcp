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

# --- Gramps Web API (required at runtime; set with -e / secrets / compose) ---
# GRAMPS_API_URL      Base URL of your Gramps Web instance (no trailing slash).
# GRAMPS_USERNAME     API user name.
# GRAMPS_PASSWORD     API password or token.
# GRAMPS_TREE_ID      Tree UUID on that server.
# GRAMPS_READ_ONLY    true|false — when true, block create/update/delete tool calls.
#
# You can also enable read-only mode with a container app argument:
# docker run ... gramps-web-mcp --read-only

# --- MCP transport (optional; these defaults suit a networked container) ---
# MCP_TRANSPORT       stdio | http | sse — default in image is http for Docker.
# ASPNETCORE_URLS     Listen addresses for http/sse (bind all interfaces in containers).
# MCP_PATH            URL prefix for MCP endpoints (default /mcp).
# MCP_STATELESS       true|false — Streamable HTTP stateless mode (default true for http).
# MCP_ENABLE_LEGACY_SSE  true|false — with http, also expose legacy /sse + /message.

ENV MCP_TRANSPORT=http \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    MCP_PATH=/mcp

EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "GrampsWeb.Mcp.dll"]
