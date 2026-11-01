#!/usr/bin/env bash
# Lint the tool definitions with Glama's TDQS (Tool Definition Quality Score,
# https://tdqs.dev): descriptions, parameter documentation, annotations and
# tools that shadow each other, as an MCP client lists them. It is the
# deterministic part of the score Glama shows and needs no model or key.
#
# Usage, after a Release build, with Node.js 22 or later:
#   dotnet build GrampsWeb.Mcp -c Release
#   npm ci --prefix scripts/tdqs --ignore-scripts
#   ./scripts/lint-tools.sh [--format text|markdown|json] [--output <file>]
#
# The server starts with every tool listed (editing and media uploads on) and
# a Gramps Web address nothing answers, which listing the tools never calls.
# Errors and warnings fail; notes, such as no-output-schema for the tools that
# return text, pass. Exit code: 0 when it passes, 1 on an error or warning,
# 2 when the server does not start ("Connection closed": run the env command
# below by hand to see why) or on another failure. CI runs it on every push
# and pull request.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DLL="$ROOT/GrampsWeb.Mcp/bin/Release/net10.0/GrampsWeb.Mcp.dll"
TDQS="$ROOT/scripts/tdqs/node_modules/.bin/tdqs"

if [ ! -f "$DLL" ]; then
  echo "error: no Release build, run: dotnet build GrampsWeb.Mcp -c Release" >&2
  exit 2
fi
if [ ! -x "$TDQS" ]; then
  echo "error: mcp-tdqs is not installed, run: npm ci --prefix scripts/tdqs --ignore-scripts" >&2
  exit 2
fi

# tdqs starts the server with a minimal environment (HOME, PATH and a few
# more), not this one, so env passes the settings.
SETTINGS="GRAMPS_API_URL=http://127.0.0.1:9 GRAMPS_USERNAME=lint GRAMPS_PASSWORD=lint GRAMPS_TREE_ID=lint"
SETTINGS+=" GRAMPS_MEDIA_RESOURCES_ENABLED=true GRAMPS_MEDIA_UPLOAD_DIRS='${TMPDIR:-/tmp}'"

exec "$TDQS" lint --server-name gramps-web-mcp --fail-on warning "$@" \
  --command "env $SETTINGS dotnet '$DLL'"
