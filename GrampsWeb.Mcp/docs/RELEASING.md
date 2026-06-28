# Releasing

Use this checklist when publishing a new `gramps-web-mcp` release. The release
tag drives the Docker image, GitHub Release assets, MCPB bundles, and MCP
Registry publishing, so keep the version metadata aligned before tagging.

## Before You Tag

- Choose the next semantic version, for example `1.0.4`.
- Update `CHANGELOG.md`:
  - Move relevant entries from `Unreleased` into a dated `## [x.y.z] - YYYY-MM-DD`
    section.
  - Update the `[Unreleased]` compare link to start at `vx.y.z`.
  - Add a `[x.y.z]` compare link from the previous tag.
- Update `GrampsWeb.Mcp/GrampsWeb.Mcp.csproj`:
  - Set `<Version>x.y.z</Version>`.
- Update `server.json`:
  - Set `"version": "x.y.z"`.
  - Set the OCI package identifier to
    `"ghcr.io/scormave/gramps-web-mcp:x.y.z"`.
- Check whether the Docker runtime surface changed:
  - Update `Dockerfile` comments, labels, exposed ports, health check, or default
    environment variables if needed.
  - Update `docker-compose.example.yml`, `.env.example`, and `README.md` if new
    runtime configuration is user-visible.
- Check whether the MCPB surface changed:
  - Update `mcpb/manifest.template.json` for new prompts, tools, user config,
    compatibility, privacy policy links, or descriptions.
  - Update `mcpb/README.md` and `README.md` if install or configuration steps
    changed.
- Check whether MCP Registry metadata changed:
  - Confirm `server.json` still matches the current schema and package layout.
  - Confirm the Docker image name in `server.json` matches the tag that will be
    pushed by `.github/workflows/docker.yml`.

## Local Verification

Run the normal build and test loop:

```bash
dotnet restore gramps-web-mcp.sln
dotnet build gramps-web-mcp.sln --no-restore -c Release
dotnet test gramps-web-mcp.sln --no-build -c Release --verbosity normal
```

Optionally smoke-test packaging before tagging:

```bash
docker build -t ghcr.io/scormave/gramps-web-mcp:x.y.z .
./scripts/pack-mcpb.sh osx-arm64 x.y.z
```

If you have the MCPB CLI installed, `pack-mcpb.sh` validates the generated
manifest before packing.

## Create The Release

Create the release commit and tag:

```bash
git status
git add CHANGELOG.md GrampsWeb.Mcp/GrampsWeb.Mcp.csproj server.json GrampsWeb.Mcp/docs/RELEASING.md
git commit -m "Release x.y.z"
git tag vx.y.z
git push origin main --tags
```

Adjust the branch name if the release branch is not `main`.

## Watch Automation

After the tag push, verify these GitHub Actions complete successfully:

- `.github/workflows/docker.yml`
  - Runs tests.
  - Builds and pushes `ghcr.io/scormave/gramps-web-mcp:x.y.z`.
  - Publishes `ghcr.io/scormave/gramps-web-mcp:latest` when the default branch is
    released.
  - Publishes `server.json` to the MCP Registry on `v*` tags.
- `.github/workflows/mcpb-release.yml`
  - Builds platform MCPB bundles for `osx-arm64`, `osx-x64`, and `win-x64`.
  - Attaches `dist/*.mcpb` assets to the GitHub Release for the tag.

If the Gitea mirror is in use, also verify `.gitea/workflows/docker.yml` pushes
the equivalent tagged image to the configured container registry.

## Post-Release Checks

- Open the GitHub Release for `vx.y.z`:
  - Confirm generated release notes look reasonable.
  - Confirm all three MCPB assets are attached.
- Check GHCR:
  - Confirm `ghcr.io/scormave/gramps-web-mcp:x.y.z` exists.
  - Confirm `latest` points at the expected image when releasing from the default
    branch.
- Check the MCP Registry:
  - Confirm the published version is `x.y.z`.
  - Confirm the package identifier points to
    `ghcr.io/scormave/gramps-web-mcp:x.y.z`.
- Optionally install a released MCPB bundle in Claude Desktop and verify the
  settings form, stdio launch, and basic read-only tools work.
- Optionally run the released Docker image against a Gramps Web instance and
  verify `/health` and the MCP endpoint.

## If Something Fails

- Do not move an existing release tag unless you are certain nobody has consumed
  it yet.
- Prefer fixing the issue in a new patch release.
- If a workflow failed before publishing public artifacts, fix the branch and
  re-run the failed workflow or push a replacement tag only after confirming the
  failed tag was not used externally.
