# Contributing

Thank you for your interest in contributing to gramps-web-mcp.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Getting started

```bash
git clone https://github.com/Scormave/gramps-web-mcp.git
cd gramps-web-mcp
./run-local-server.sh   # HTTP transport on http://127.0.0.1:8080 (demo server)
```

## Running tests

```bash
dotnet test
```

Contract tests only:

```bash
dotnet test --filter "Category=Contract"
```

## Development guide

See [GrampsWeb.Mcp/docs/DEVELOPER_GUIDE.md](GrampsWeb.Mcp/docs/DEVELOPER_GUIDE.md) for:

- Project conventions (naming, tool signatures, error handling)
- How to add new tools, models, formatters, and Flexible* input types
- Update semantics for write tools (omit vs empty list)
- Testing strategy and fixture files

Additional reference:

- [Architecture](GrampsWeb.Mcp/docs/ARCHITECTURE.md)
- [Tool catalog](GrampsWeb.Mcp/docs/TOOL_CATALOG.md)
- [API inventory](GrampsWeb.Mcp/docs/API_INVENTORY.md)
- [Data model](GrampsWeb.Mcp/docs/DATA_MODEL.md)

## Pull requests

1. Fork the repository and create a feature branch from `main`.
2. Make focused changes with tests where appropriate.
3. Run `dotnet test` and ensure all tests pass.
4. Update documentation if you change tool behavior, API usage, or configuration.
5. Open a pull request with a clear description of the change and why it is needed.

## Vendored API spec

`apispec.yaml` at the repository root is the upstream Gramps Web OpenAPI spec.
Do not edit it in place. When the upstream spec changes, replace the file
wholesale from Gramps Web and update `GrampsWeb.Mcp.Tests/Contract/swagger-dto-map.json`
if DTO mappings change.

## Code of conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md).
By participating, you agree to uphold it.
