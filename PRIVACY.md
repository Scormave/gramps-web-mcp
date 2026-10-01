# Privacy Policy — Gramps Web MCP (Claude Desktop Extension)

**Last updated:** 2026-10-01

## Summary

Gramps Web MCP is a **local** Model Context Protocol server. It runs on your computer inside Claude Desktop and connects only to the Gramps Web instance you configure.

## What data is collected

This extension **does not** collect, store, or transmit data to Scormave or any third party except:

1. **Your configured Gramps Web server** — API requests (including credentials you supply) are sent directly from your machine to `GRAMPS_API_URL` to read or modify genealogy data you authorize.
2. **Claude Desktop / Anthropic** — governed by [Anthropic's privacy policy](https://www.anthropic.com/legal/privacy) when you use Claude. Tool inputs and outputs may be processed by the model according to your Claude settings.

The extension does **not** phone home, run analytics, or log conversations to external servers.

## Credentials and storage

- Gramps Web username, password/token, and tree ID are entered in Claude Desktop's extension settings UI.
- Sensitive values are stored in your operating system's secure credential storage by Claude Desktop, not in plain-text project files.
- Credentials are used only to authenticate against your Gramps Web API.

## Media and sensitive genealogy data

- Media file downloads are **disabled by default**. Enable them only if you understand photos and documents may be sent to Claude as tool output.
- Private media records remain blocked unless you explicitly enable `Allow private media`.
- Thumbnails are rendered by the extension from the original file with EXIF (including GPS location) and other embedded metadata removed. Mode `file` sends the original file unchanged, metadata included.
- Genealogy data may include names, dates, places, and family relationships of living or deceased persons. You are responsible for compliance with applicable privacy laws when exposing this data to AI tools.

## Data retention

- The extension does not maintain its own database.
- The extension writes diagnostic logs to stderr, which Claude Desktop saves to a local log file on your computer. By default these logs contain only request metadata (method, path without query string, status, duration, body size), not your tree data.
- Request and response bodies, which contain tree data, are logged only if you set `GRAMPS_LOG_LEVEL=Debug` or `Trace`. Fields that look like credentials are redacted even then.
- Gramps Web retains data according to your Gramps Web deployment.
- Claude conversation retention follows your Anthropic/Claude account settings.

## Third-party services

| Service | Role | Policy |
|---------|------|--------|
| Your Gramps Web instance | Primary data store and API | Your deployment / [Gramps Web project](https://github.com/gramps-project/gramps-web) |
| Claude Desktop (Anthropic) | MCP host and AI interface | [Anthropic Privacy Policy](https://www.anthropic.com/legal/privacy) |

## Open source

Source code is available at [github.com/Scormave/gramps-web-mcp](https://github.com/Scormave/gramps-web-mcp) under AGPL-3.0-or-later.

## Contact

- Security issues: [GitHub Security Advisories](https://github.com/Scormave/gramps-web-mcp/security/advisories/new)
- General support: [GitHub Issues](https://github.com/Scormave/gramps-web-mcp/issues)
