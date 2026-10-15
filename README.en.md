<div align="center">

# LaunchDesk

**An open-source Windows launcher for local applications and services**

Start scripts, commands and programs together. Keep their status and logs in one place.

[简体中文](README.md) | [English](README.en.md)

[![Release](https://img.shields.io/github/v/release/hailaij/LaunchDesk?style=flat-square)](https://github.com/hailaij/LaunchDesk/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/hailaij/LaunchDesk/total?style=flat-square)](https://github.com/hailaij/LaunchDesk/releases)
[![Build](https://img.shields.io/github/actions/workflow/status/hailaij/LaunchDesk/windows.yml?branch=main&style=flat-square&label=CI)](https://github.com/hailaij/LaunchDesk/actions/workflows/windows.yml)
[![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?style=flat-square)](https://github.com/hailaij/LaunchDesk/releases/latest)
[![MIT](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

[Download](https://github.com/hailaij/LaunchDesk/releases/latest) · [User guide](docs/user-guide.en.md) · [Report an issue](https://github.com/hailaij/LaunchDesk/issues) · [Changelog](CHANGELOG.en.md)

</div>

![LaunchDesk English interface](docs/images/main-en.png)

> Current stable release: **v0.10.1**. Installers are **unsigned and require manual installation**; stable status is not publisher-signature certification. Screenshots use isolated fixture data, not preinstalled launch items or logs. [More screenshots](docs/screenshots.md)

## Contents

- [Overview](#overview)
- [Features](#features)
- [Supported launch items](#supported-launch-items)
- [Quick start](#quick-start)
- [Documentation](#documentation)
- [Technology and structure](#technology-and-structure)
- [Contributing](#contributing)
- [FAQ](#faq)
- [Changelog](#changelog)
- [License and privacy](#license-and-privacy)

## Overview

LaunchDesk is for Windows users who repeatedly run several local applications or background services: local bot operators, developers and lightweight operations users.

Save BAT scripts, npm commands and executables as launch items, organize them into folders, configure working directories and dependency order, and inspect their status and logs together. It is a local process manager, not a remote management platform, full interactive terminal or container orchestrator.

## Features

### Grouped launch controls

Start/stop individual items, folders or everything. Drag to reorder, edit names and notes inline, and use login startup, the tray and single-instance behavior. Folders are application groups, not moved script directories.

### Dependency startup and readiness

Check paths, interpreters and simple npm configuration before launch. Configure dependencies, startup delays and TCP, HTTP or log-keyword readiness checks. Enable ongoing health monitoring per item; automatic restart defaults to off and has rate limits and cooldowns.

### Live logs and managed processes

View stdout/stderr with search, channel filters, merged folder logs, copy and export. Pausing the view does not stop capture; Ctrl+click opens log links. Windows Job Objects manage process trees. Shutdown attempts a graceful stop and drains output before terminating the managed tree on timeout.

### Data, appearance and advanced workflows

Import/export configurations, keep automatic/manual backups, use portable mode or custom data directories. Includes Chinese/English, light/dark/system themes, CLI, crash recovery, per-item UAC elevation and separate background service hosting. See [service and permission boundaries](docs/service-accessibility-installer.md).

## Supported Launch Items

| Type | Example | Notes |
|---|---|---|
| BAT / CMD | `C:\Services\Chat\launcher.bat` | Defaults to the script directory if no working directory is set |
| Command | `npm` with arguments `run start` | Use the project containing the required npm script |
| Executable | `C:\Tools\worker.exe` | GUI logging requires actual stdout/stderr writes |
| PowerShell script | `C:\Scripts\worker.ps1` | Does not bypass execution policy |
| Shell script | `C:\Scripts\worker.sh` | Specify an executable interpreter such as Git Bash |

Each item has its own arguments, working directory, environment, encoding, stop input and timeout. Install dependencies such as Node.js or Python separately.

## Quick Start

### Download and install

1. Open the [latest stable release](https://github.com/hailaij/LaunchDesk/releases/latest).
2. Download `LaunchDesk-v0.10.1-win-x64-setup.exe` or the portable `LaunchDesk-v0.10.1-win-x64.zip`.
3. Windows 10/11 x64 self-contained packages include .NET. Fully extract the ZIP before running `LaunchDesk.exe`.
4. Add a launch item, enter the command/resource, arguments and working directory, then start it.

The per-user installer defaults to `%LOCALAPPDATA%\Programs\LaunchDesk`. Back up configuration and exit the old tray instance before upgrading; this stops desktop-managed launch items. Preserve `portable.flag` and `data\` when using portable mode.

> Update checks retrieve stable release metadata only. A user click opens the download page in the default browser for manual download and installation. The app does not download, verify or execute installers, or automatically launch a browser. SHA-256 checks integrity, not publisher identity.

### Build from source

Requires Windows x64 and **.NET SDK 10.0.401** as specified in `global.json`. Installer compilation also requires Inno Setup 6.

```powershell
git clone https://github.com/hailaij/LaunchDesk.git
cd LaunchDesk
dotnet restore LaunchDesk.slnx --locked-mode
dotnet build LaunchDesk.slnx -c Release
dotnet run --project src/LaunchDesk.App -c Release
```

See [advanced usage and development](docs/advanced-guide.en.md) for packaging, tests and installer commands. Use a new output directory: packaging rejects nonempty publish directories.

## Documentation

| Document | Contents |
|---|---|
| [User guide](docs/user-guide.en.md) | Items, folders, logs, health monitoring and everyday operations |
| [Advanced reference](docs/advanced-guide.en.md) | Encoding, data persistence, process lifecycle, build and test commands |
| [Screenshots](docs/screenshots.md) | Current bilingual, dark-theme and empty-first-use views |
| [Services, accessibility and installer](docs/service-accessibility-installer.md) | CLI, elevation, service hosting and acceptance boundaries |
| [Project structure](docs/project-structure.md) | Source, tests, scripts and generated files |
| [Update boundaries](docs/update-security.md) | Metadata checks, browser downloads and independent signing gates |
| [Release verification](docs/release-v0.10.1.md) | Release provenance, verification scope and known limitations |
| [Test isolation and scale](docs/scale-and-test-gates.md) | Deadlines, resource keys, 1,000-item and 200 MiB gates |
| [Security](SECURITY.md) / [Privacy](PRIVACY.md) | Vulnerability reporting, network and local data handling |

## Technology And Structure

| Component | Technology / responsibility |
|---|---|
| Desktop UI | C#, .NET 10, WPF |
| Core management | Windows Job Objects, dual-channel logs, JSON configuration and backups |
| Background hosting | Separate Host, Windows services and elevation helpers |
| Installation and validation | Inno Setup 6, PowerShell, GitHub Actions |

`src/LaunchDesk.App` contains the desktop UI, `LaunchDesk.Core` the core logic, `LaunchDesk.Cli` the command-line entry, and `LaunchDesk.Host` the background host. `tests/`, `scripts/`, `tools/` and `docs/` contain regressions, build scripts, release checks and documentation. Generated files belong in `artifacts/`.

`main` contains current development and release documentation; `dev` preserves history through v0.8.1. Existing tags and Releases remain unchanged.

## Contributing

Submit an [issue](https://github.com/hailaij/LaunchDesk/issues) or pull request. Include the application version, Windows version, reproduction steps and sanitized error excerpts. Do not publish tokens, environment values, private configurations or full logs.

Read the [development reference](docs/advanced-guide.en.md) and run relevant isolated regressions. The core inventory has 128 groups; three explicitly skip without isolated signing fixtures, and strict CI rejects skips. Never install test trust roots on a developer workstation.

## FAQ

**npm reports Missing script: start?**

Check that the working directory is the correct project and its `package.json` defines `scripts.start`. Treating npm as an executable is not the solution.

**Why is LaunchDesk still running after closing the window?**

Closing hides it in the tray. Use the tray's Exit command to terminate the application and its desktop-managed items.

**Where are configurations and logs?**

The default is `%LOCALAPPDATA%\LaunchDesk`; portable mode uses `data\` beside the program. Settings show the actual directory and provide data/backup shortcuts. Uninstall retains user data.

**Does it manage WSL, Docker or remote servers?**

It does not manage their internal lifecycles or provide remote administration. Externally hosted processes and existing GUI single-instance processes may be outside the Job. File-only logs are not automatically captured as stdout/stderr.

**Is the stable release signed and fully accepted on physical machines?**

No. Packages remain unsigned. Physical Windows 10, real services, UAC/SCM and screen-reader acceptance are pending. A prior development source passed a one-hour soak, but it was not repeated on this release source. Historical intermittent npm/PowerShell exit timeouts remain unexplained; subsequent passes do not prove a root-cause fix. See the [release verification record](https://github.com/hailaij/LaunchDesk/releases/tag/v0.10.1).

## Changelog

Full history lives in [CHANGELOG.en.md](CHANGELOG.en.md), with [Chinese history](CHANGELOG.md) alongside it. Download artifacts and verification records are on [Releases](https://github.com/hailaij/LaunchDesk/releases).

## License And Privacy

[MIT License](LICENSE). Third-party/runtime terms: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Data and network behavior: [PRIVACY.md](PRIVACY.md). Report security issues according to [SECURITY.md](SECURITY.md).

Share real use cases, report problems or contribute improvements when LaunchDesk helps your workflow.
