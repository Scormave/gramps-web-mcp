# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.0.2] - 2026-06-26

### Added

- Claude Desktop MCPB (`.mcpb`) packaging with self-contained binaries for macOS and Windows
- Tag-driven GitHub Release workflow for per-platform desktop extension bundles
- [`PRIVACY.md`](PRIVACY.md) and MCPB manifest for Connectors Directory readiness
- Explicit MCP tool annotations (`title`, `readOnlyHint`, `destructiveHint`) on all 57 tools

### Changed

- Upgraded `ModelContextProtocol` packages to 1.3.0 for tool annotation support

## [1.0.1] - 2026-06-24

### Added

- `GET /health` HTTP endpoint for Docker `HEALTHCHECK` and Unraid monitoring
- Startup log line confirming Gramps Web connectivity (`Connected to Gramps Web at …`)
- [`docker-compose.example.yml`](docker-compose.example.yml) stack for Gramps Web + MCP on one network
- MCP Registry metadata and automated publishing on version tags
- OCI image ownership label for MCP Registry validation

### Changed

- Docker release workflow now publishes the MCP Registry entry after tagged image builds

## [1.0.0] - 2026-06-23

### Added

- Initial public release of the Gramps Web MCP server (.NET 8)
- 57 MCP tools for people, families, events, places, sources, citations, notes,
  media, repositories, tags, search, and composite workflows
- MCP resources (`gramps://input-guide`, `gramps://types`, `gramps://metadata`,
  `gramps://name-settings`) and compatibility tools
- Opt-in binary MCP resources for media thumbnails and full files, guarded by
  size limits, MIME allowlists, and private-record defaults.
- Open WebUI-compatible media image tools for thumbnail and full-file photo
  analysis when MCP resources are not available to the client.
- MCP prompts for common genealogy workflows
- stdio, Streamable HTTP, and legacy SSE transports
- Read-only mode that keeps tools visible while blocking mutation calls
- Docker image published to `ghcr.io/scormave/gramps-web-mcp`
- Contract tests against vendored Gramps Web OpenAPI spec

[Unreleased]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.2...HEAD
[1.0.2]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Scormave/gramps-web-mcp/releases/tag/v1.0.0
