# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Opt-in binary MCP resources for media thumbnails and full files, guarded by
  size limits, MIME allowlists, and private-record defaults.
- Open WebUI-compatible media image tools for thumbnail and full-file photo
  analysis when MCP resources are not available to the client.

## [0.1.0] - 2026-06-06

### Added

- Initial public release of the Gramps Web MCP server (.NET 8)
- 55 MCP tools for people, families, events, places, sources, citations, notes,
  media, repositories, tags, search, and composite workflows
- MCP resources (`gramps://input-guide`, `gramps://types`, `gramps://metadata`,
  `gramps://name-settings`) and compatibility tools
- MCP prompts for common genealogy workflows
- stdio, Streamable HTTP, and legacy SSE transports
- Docker image published to `ghcr.io/scormave/gramps-web-mcp`
- Contract tests against vendored Gramps Web OpenAPI spec

[Unreleased]: https://github.com/Scormave/gramps-web-mcp/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Scormave/gramps-web-mcp/releases/tag/v0.1.0
