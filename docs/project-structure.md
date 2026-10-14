# Project Structure

```text
LaunchDesk/
  .github/workflows/       Windows CI
  installer/              Inno Setup installer definition
  src/                    Application and process-management source
  tests/                  Isolated integration tests
  scripts/                Packaging and icon-generation tools
  docs/                   Documentation, images and test evidence
  artifacts/              Local generated files (ignored by Git)
    LaunchDesk-win-x64/    Default publish output; not necessarily the latest preview
    test-results.json     Default test-report output
    previews/             Unreleased application builds
    installers/           Local setup EXE, checksum and isolated validation
    releases/             Historical versioned release bundles and evidence
    verification/         UI screenshots, test reports and download checks
    archive/              Earlier build outputs retained for reference
    local/                Machine-specific example configuration (do not publish)
    organization/         Move manifest for this directory reorganization
  LaunchDesk.slnx          Solution entry point
  Directory.Build.props   Shared version and build properties
  global.json             SDK version
  NuGet.Config            Package sources
  README.md               Chinese product overview and quick start
  README.en.md            English product overview and quick start
  CHANGELOG.md            Complete Chinese version history
  CHANGELOG.en.md         Complete English version history
```

## Current Release Build

Build an installer with `scripts/build-installer.ps1`. The default installer is:

`artifacts/installers/LaunchDesk-v0.9.0-win-x64-setup.exe`

Keep the complete application directory when moving or copying a ZIP build. Exit an existing tray instance before installing another build; exiting stops its managed processes. Historical local previews may remain under `artifacts/previews/` and are not release downloads.

## Output Conventions

Detailed usage, lifecycle boundaries and build/test commands are in
[advanced-guide.md](advanced-guide.md) and [advanced-guide.en.md](advanced-guide.en.md).
Keep complete version records in the root CHANGELOG files; README links to them
instead of duplicating historical feature lists.

- Keep source, scripts, documentation and tests in their existing locations so project references and CI remain unchanged.
- Default build and test commands in the README continue to use their existing output paths.
- For a versioned package, use `./scripts/package.ps1 -OutputDirectory artifacts/releases/release-vX.Y.Z`.
- Use a new output directory for each build. Packaging refuses a nonempty existing payload directory to prevent stale or private files entering a release.
- For a preview, publish into `artifacts/previews/<preview-name>`.
- Store manual UI-test output in `artifacts/verification/<check-name>`.
- Never publish local configurations, smoke-test data or logs without checking for private paths and environment variables.
- Historical outputs were moved, not deleted. `artifacts/organization/moves-2026-10-11.json` records original and new paths for the reorganization.
- `bin/` and `obj/` are compiler-managed directories and remain alongside their projects.
