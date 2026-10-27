# Releasing

Use this checklist when publishing a new `gramps-web-mcp` release. The release
tag drives the Docker image, GitHub Release assets, MCPB bundles, and MCP
Registry publishing, so keep the version metadata aligned before tagging.

## Before You Tag

- Choose the next semantic version, for example `2.1.1`.
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
  - The package intentionally declares `stdio` with `MCP_TRANSPORT=stdio`: the
    registry describes containers that a client spawns itself. The image's own
    default of `MCP_TRANSPORT=http` on port 8080 is documented in `README.md`
    instead, because a package transport of `streamable-http` would have to pin a
    fixed host port that not every deployment uses.
- Update the example `pack-release.sh <rid> x.y.z` commands in `mcpb/README.md`
  and the usage comment of `scripts/pack-release.sh`.
- Check whether the Docker runtime surface changed:
  - Update `Dockerfile` comments, labels, exposed ports, health check, or default
    environment variables if needed.
  - Update `docker-compose.example.yml`, `.env.example`, `README.md`, and
    `site/index.html` if new runtime configuration is user-visible.
- Check whether the MCPB surface changed:
  - Update `mcpb/manifest.template.json` for new prompts, tools, user config,
    compatibility, privacy policy links, or descriptions.
  - Update `mcpb/README.md` and `README.md` if install or configuration steps
    changed.
- Check whether server dependencies changed:
  - Confirm `THIRD-PARTY-NOTICES.txt` lists each new package with its license
    and copyright, and that its upstream notice links point at the versions in
    use; the test suite checks only the package names.
- Check that the Six Labors license key in `GrampsWeb.Mcp/sixlabors.lic` is not
  due for renewal (see [Six Labors License Key](#six-labors-license-key)): Release
  builds fail once it expires, so the tag would publish no Docker image, no
  MCPB bundles and no binary archives.
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

`ReleaseVersionTests` fails until `server.json`, the changelog and the
`pack-release.sh` examples follow the `<Version>` in the csproj: a dated
changelog section for it with entries, the compare links of every version,
and the image tag in `server.json`.

With Docker running, also run the integration tests against Gramps Web; CI
runs them too:

```bash
GRAMPS_INTEGRATION_TESTS=true dotnet test GrampsWeb.Mcp.IntegrationTests --no-build -c Release
```

Optionally smoke-test packaging before tagging, as the release workflows do;
the image test needs Docker on Linux:

```bash
docker build -t gramps-web-mcp:smoke .
python3 scripts/smoke-test.py image gramps-web-mcp:smoke
./scripts/pack-release.sh osx-arm64 x.y.z
python3 scripts/smoke-test.py mcpb dist/gramps-web-mcp-claude-desktop-osx-arm64-vx.y.z.mcpb
python3 scripts/smoke-test.py archive dist/gramps-web-mcp-osx-arm64.tar.gz
```

`scripts/smoke-test.py` starts the server against a stand-in for Gramps Web
and checks that it answers MCP: the image over HTTP, with its API key, origin
check and `HEALTHCHECK` command, and over stdio; the bundle over stdio, started
from its `manifest.json` with the default settings that Claude Desktop passes;
the binary archive over stdio, started with only the Gramps settings in its
environment.
Its header lists every check. A manual run of the release assets workflow on
`main` (`gh workflow run release-assets.yml`) packs and smoke-tests the
bundles and archives on GitHub without a release.

If you have the MCPB CLI installed, `pack-release.sh` validates the generated
manifest before packing. The pack restores the project for one runtime, which
adds runtime-only packages to `obj/project.assets.json`; run
`dotnet restore gramps-web-mcp.sln` before testing again, or the
third-party notices test fails.

## Create The Release

Create the release commit and tag. Add every file the checklist above touched;
at minimum:

```bash
git status
git add CHANGELOG.md GrampsWeb.Mcp/GrampsWeb.Mcp.csproj server.json mcpb/README.md scripts/pack-release.sh
git commit -m "Release x.y.z"
./scripts/check-release-version.sh vx.y.z
git tag vx.y.z
git push origin main --tags
```

Adjust the branch name if the release branch is not `main`.

`scripts/check-release-version.sh` passes when the tag is `vx.y.z`, the
csproj declares `<Version>x.y.z</Version>` and `CHANGELOG.md` has a dated
`## [x.y.z]` section. The release workflows run it on every `v*` tag before
they build anything, so a tag pushed without the release commit fails before
it publishes an image, MCPB bundles or a registry entry (see
[If Something Fails](#if-something-fails)).

## Watch Automation

After the tag push, verify these GitHub Actions complete successfully:

- `.github/workflows/docker.yml` (one run for the `main` push, one for the tag)
  - The tag run first checks the tag with `scripts/check-release-version.sh`.
  - Runs tests.
  - Builds the amd64 image and smoke-tests it before it pushes anything.
  - The tag run builds and pushes `ghcr.io/scormave/gramps-web-mcp:x.y.z` and
    publishes `server.json` to the MCP Registry.
  - The `main` run builds and pushes `ghcr.io/scormave/gramps-web-mcp:latest`;
    tag runs never move `latest`.
  - Both runs attest the pushed image; the run summary links the attestation.
- `.github/workflows/release-assets.yml`
  - Checks the tag with `scripts/check-release-version.sh`.
  - Builds platform MCPB bundles and binary archives for `osx-arm64`,
    `osx-x64`, `win-x64`, `linux-x64`, and `linux-arm64`.
  - Smoke-tests the `linux-x64` and `osx-arm64` bundles, which its runners can
    start, as Claude Desktop starts them, and their archives as other clients
    start the binary.
  - Attests the bundles, the archives and `SHA256SUMS`, then attaches them to
    the GitHub Release for the tag.

If the Gitea mirror is in use, also verify `.gitea/workflows/docker.yml` pushes
the equivalent tagged image to the configured container registry.

## Post-Release Checks

- Open the GitHub Release for `vx.y.z`:
  - Confirm generated release notes look reasonable.
  - Confirm all five MCPB bundles, all five binary archives and `SHA256SUMS`
    are attached.
  - Download one archive and confirm
    `gh attestation verify gramps-web-mcp-linux-x64.tar.gz --repo Scormave/gramps-web-mcp`
    passes.
- Check GHCR:
  - Confirm `ghcr.io/scormave/gramps-web-mcp:x.y.z` exists.
  - Confirm `latest` points at the image built from the release commit on the
    default branch.
  - Confirm
    `gh attestation verify oci://ghcr.io/scormave/gramps-web-mcp:x.y.z --repo Scormave/gramps-web-mcp`
    passes.
- Check the MCP Registry:
  - Confirm the published version is `x.y.z`.
  - Confirm the package identifier points to
    `ghcr.io/scormave/gramps-web-mcp:x.y.z`.
- Optionally install a released MCPB bundle in Claude Desktop and verify the
  settings form, stdio launch, and basic read-only tools work.
- Optionally run the released Docker image against a Gramps Web instance and
  verify `/health` and the MCP endpoint.
- Run the image scan for the new release (`gh workflow run image-scan.yml`),
  or leave it to the Monday run (see [Image Scan Findings](#image-scan-findings)).

## Image Scan Findings

`.github/workflows/image-scan.yml` scans the image of the latest GitHub Release
and `latest` with Trivy every Monday. When an Ubuntu package or a .NET library
in either image has a HIGH or CRITICAL vulnerability with a fix, the run fails,
its summary lists the vulnerabilities, and the Security tab shows them under
code scanning. Published images are never rebuilt, so the fix is a new build:

- Ubuntu packages and the .NET runtime come from the base image
  `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, which Microsoft
  rebuilds when Ubuntu or .NET ship a fix. Rebuild `latest` with
  `gh workflow run docker.yml`, and publish a patch release for the release
  image. If Microsoft has not rebuilt the base image yet, the next Monday's run
  checks again.
- A NuGet package of the server also fails `ci.yml`; update it and publish a
  patch release.
- To accept a vulnerability that does not affect the server, add its ID with a
  comment to `.trivyignore` at the repository root.

The alerts close on their own once a scan no longer finds them.

## If Something Fails

- Do not move an existing release tag unless you are certain nobody has consumed
  it yet.
- Prefer fixing the issue in a new patch release.
- A failed smoke test stops its workflow before it publishes anything. The
  step's log shows the checks that passed, the one that failed, the server's
  log and the requests the stand-in Gramps Web received.
- If an attestation step fails, for example while Sigstore is down, re-run the
  failed jobs. The release files are attached only after their attestation;
  the image is already pushed, and the re-run pushes it again before it
  attests, while the MCP Registry entry waits for the image job.
- If a workflow failed before publishing public artifacts, fix the branch and
  re-run the failed workflow or push a replacement tag only after confirming the
  failed tag was not used externally.

## Six Labors License Key

ImageSharp 4 refuses to compile `GrampsWeb.Mcp` in Release without a valid Six
Labors license key; a Debug build only warns. The repository commits a free
community key, scoped to the `GrampsWeb.Mcp` and `GrampsWeb.Mcp.Tests`
assemblies, as `GrampsWeb.Mcp/sixlabors.lic`, and the build finds it on its own.
The key stops working after its `ExpiryDateUtc`. Binaries built before then keep
running, because the check happens only at build time.

Renewal opens 90 days before the expiry date. From then on
`.github/workflows/license-key.yml`, which runs every Monday, keeps an issue
titled "Renew the Six Labors license key" open, and in the last 30 days the
workflow and `SixLaborsLicenseTests` fail. GitHub turns off scheduled workflows
after 60 days without repository activity, so the test is the backstop.

To renew:

- Apply again at <https://licensing.sixlabors.com/> with the same email as
  before, for the assemblies `GrampsWeb.Mcp` and `GrampsWeb.Mcp.Tests`.
  Open-source projects are approved automatically.
- Replace `GrampsWeb.Mcp/sixlabors.lic` with the new key; keep the file name and
  location.
- Run `dotnet build gramps-web-mcp.sln -c Release`. The detailed build log
  (`-v detailed`) shows `Six Labors license validated` with the new key's ID.
- Commit the file and close the reminder issue.

Renaming the `GrampsWeb.Mcp` assembly, or referencing ImageSharp directly from
another project, needs a key that names that assembly.
