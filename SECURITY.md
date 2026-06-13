# Security Policy

## Supported versions

Security fixes are applied to the latest release on the `main` branch.

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, use [GitHub Security Advisories](https://github.com/Scormave/gramps-web-mcp/security/advisories/new)
to report a vulnerability privately, or contact the maintainers through the
repository owner profile.

Include as much detail as possible:

- Description of the vulnerability and potential impact
- Steps to reproduce
- Affected versions or commits
- Suggested fix, if you have one

We aim to acknowledge reports within a few business days and will work with you
on a fix and coordinated disclosure timeline.

## Scope

This policy covers the gramps-web-mcp server code and its default configuration.
Issues in Gramps Web itself should be reported to the [Gramps Web project](https://github.com/gramps-project/gramps-web).

## Media file access

Binary media resources are disabled by default. Operators must set
`GRAMPS_MEDIA_RESOURCES_ENABLED=true` before MCP clients can read thumbnails or
full media files. Keep `GRAMPS_MEDIA_MAX_BYTES`,
`GRAMPS_MEDIA_ALLOWED_MIME_TYPES`, and `GRAMPS_MEDIA_ALLOW_PRIVATE` set
conservatively for the deployment.

Media files can contain sensitive photos, scans, and documents. The server
blocks bytes for Gramps media records marked private unless
`GRAMPS_MEDIA_ALLOW_PRIVATE=true`; metadata access through `get_media` remains
available. End-to-end exposure also depends on the MCP client and model that
receive the binary resource content.
