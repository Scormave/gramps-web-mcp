#!/usr/bin/env bash
# Check that a release tag matches the version committed in the repository.
#
# Usage:
#   ./scripts/check-release-version.sh <tag>
#
# Example:
#   ./scripts/check-release-version.sh v2.6.0
#
# Passes when the tag is v<version>, GrampsWeb.Mcp/GrampsWeb.Mcp.csproj declares
# <Version><version></Version>, and CHANGELOG.md has a dated
# "## [<version>] - YYYY-MM-DD" section. The release workflows run it on v*
# tags before they build anything; ReleaseVersionTests keeps server.json, the
# changelog links and the pack-mcpb.sh examples in step with the csproj.

set -euo pipefail

TAG="${1:?Release tag required, for example v2.6.0}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CSPROJ="$ROOT/GrampsWeb.Mcp/GrampsWeb.Mcp.csproj"
CHANGELOG="$ROOT/CHANGELOG.md"

if [[ ! "$TAG" =~ ^v([0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?)$ ]]; then
  echo "error: release tag '$TAG' is not v<major>.<minor>.<patch>, for example v2.6.0" >&2
  exit 1
fi
VERSION="${BASH_REMATCH[1]}"

FAILED=0

PROJECT_VERSION="$(grep -o '<Version>[^<]*</Version>' "$CSPROJ" | sed 's/<[^>]*>//g' | head -1)"
if [[ "$PROJECT_VERSION" != "$VERSION" ]]; then
  echo "error: GrampsWeb.Mcp/GrampsWeb.Mcp.csproj declares <Version>${PROJECT_VERSION:-(none)}</Version>, not $VERSION" >&2
  FAILED=1
fi

if ! grep -Eq "^## \[${VERSION//./\\.}\] - [0-9]{4}-[0-9]{2}-[0-9]{2}[[:space:]]*$" "$CHANGELOG"; then
  echo "error: CHANGELOG.md has no '## [$VERSION] - YYYY-MM-DD' section" >&2
  FAILED=1
fi

if [[ "$FAILED" -ne 0 ]]; then
  echo "Commit the release changes listed in GrampsWeb.Mcp/docs/RELEASING.md before tagging $TAG." >&2
  exit 1
fi

echo "Release tag $TAG matches version $VERSION."
