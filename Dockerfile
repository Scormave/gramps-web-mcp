# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY gramps-web-mcp.sln ./
COPY GrampsWeb.Mcp/GrampsWeb.Mcp.csproj GrampsWeb.Mcp/
RUN dotnet restore GrampsWeb.Mcp/GrampsWeb.Mcp.csproj

COPY GrampsWeb.Mcp/ GrampsWeb.Mcp/
RUN dotnet publish GrampsWeb.Mcp/GrampsWeb.Mcp.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Chiseled Ubuntu: only the .NET runtime and its libraries, no shell or package
# manager, non-root by default. Globalization runs in invariant mode (no ICU).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app

LABEL io.modelcontextprotocol.server.name="io.github.Scormave/gramps-web-mcp"

# --- Gramps Web API (required at runtime; set with -e / secrets / compose) ---
# GRAMPS_API_URL      Base URL of your Gramps Web instance (no trailing slash).
# GRAMPS_USERNAME     API user name.
# GRAMPS_PASSWORD     API password.
# GRAMPS_REFRESH_TOKEN  Refresh token; replaces username and password when set.
# GRAMPS_TREE_ID      Tree UUID on that server.
# GRAMPS_READ_ONLY    true|false — when true, block create/update/delete tool calls.
# GRAMPS_MUTATION_SERIALIZE  true|false — serialize mutation HTTP calls in-process (default true).
# GRAMPS_MUTATION_MIN_INTERVAL_MS  Minimum ms between mutation HTTP calls (default 0; SQLite often 250).
# GRAMPS_LOG_LEVEL    Server log level (default Information); Debug/Trace also log API bodies.
# GRAMPS_MEDIA_RESOURCES_ENABLED  true|false — expose read_media, media resources and uploads (default false).
# GRAMPS_MEDIA_MAX_BYTES  Max bytes returned by read_media or a media resource (default 5242880).
# GRAMPS_MEDIA_ALLOW_PRIVATE  true|false — return bytes for media marked private (default false).
# GRAMPS_MEDIA_UPLOAD_DIRS  Mounted folders create_media may upload from, :-separated (default unset: off; needs media access).
# GRAMPS_MEDIA_UPLOAD_MAX_BYTES  Largest file create_media or update_media uploads (default 52428800).

# --- MCP transport (optional; these defaults suit a networked container) ---
# MCP_TRANSPORT       stdio | http | sse — default in image is http for Docker.
# ASPNETCORE_URLS     Listen addresses for http/sse (bind all interfaces in containers).
# MCP_PATH            URL prefix for MCP endpoints (default /mcp).
# MCP_STATELESS       true|false — Streamable HTTP stateless mode (default true for http).
# MCP_ENABLE_LEGACY_SSE  true|false — with http, also expose legacy /sse + /message.
# MCP_API_KEY         Optional shared secret for HTTP/SSE transport (min 16 chars; comma-separated for rotation).
# MCP_ALLOWED_ORIGINS Browser origins allowed to call HTTP/SSE, comma-separated (default none; others get 403).
# GET /health         Liveness/readiness probe; minimal JSON {"status": ...}.
#                     Inside the container: dotnet /app/GrampsWeb.Mcp.dll --health-check

ENV MCP_TRANSPORT=http \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    MCP_PATH=/mcp

EXPOSE 8080

COPY --from=build /app/publish .
COPY LICENSE THIRD-PARTY-NOTICES.txt ./
USER $APP_UID

# The image has no curl: the server checks its own GET /health, finding the
# port from ASPNETCORE_URLS, and exits 0 on 200 and 1 otherwise.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD ["dotnet", "/app/GrampsWeb.Mcp.dll", "--health-check"]

ENTRYPOINT ["dotnet", "GrampsWeb.Mcp.dll"]
