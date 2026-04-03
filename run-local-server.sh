#!/usr/bin/env bash
set -euo pipefail

# Local Gramps demo credentials and MCP server transport defaults.
# Values are intentionally aligned with test-config.sh.
export GRAMPS_API_URL="https://demo.grampsweb.org"
export GRAMPS_USERNAME="owner"
export GRAMPS_PASSWORD="owner"
export GRAMPS_TREE_ID="69d36818"

# Transport defaults (override before running if needed).
export MCP_TRANSPORT="${MCP_TRANSPORT:-http}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:8080}"
export MCP_PATH="${MCP_PATH:-/mcp}"

echo "Starting Gramps MCP server..."
echo "  GRAMPS_API_URL=$GRAMPS_API_URL"
echo "  GRAMPS_USERNAME=$GRAMPS_USERNAME"
echo "  GRAMPS_TREE_ID=$GRAMPS_TREE_ID"
echo "  MCP_TRANSPORT=$MCP_TRANSPORT"
echo "  ASPNETCORE_URLS=$ASPNETCORE_URLS"
echo "  MCP_PATH=$MCP_PATH"

dotnet run --project "GrampsWeb.Mcp/GrampsWeb.Mcp.csproj"
