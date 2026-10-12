# v0.8.0 Review and Verification / 复查与验证

Date: 2026-10-12. Unsigned prerelease; manual installation only.

## Review Findings / 复查修复

1. **P1: stale packaging payload.** Reusing a nonempty output directory could
   retain files absent from the new staging tree, including private configuration.
   Packaging now refuses that directory before publishing. Engineering regression
   checks the exact refusal reason, unchanged sentinel hash and absence of a ZIP.
2. **P2: service executable path validation.** Directly opening `item.Target`
   ignored its working directory/PATH. The new regression reproduced a
   `FileNotFoundException`; validation now shares `LaunchCommand.From` with actual
   startup. Both working-directory and PATH resolution pass, with GUI rejection
   unchanged.
3. **P2: native identity handles.** 200 local pipe-name queries increased the
   process handle count by 200 before repair. Identity/process objects now use
   deterministic disposal; a repeated-query regression guards handle growth.

Reviewed health/readiness lifetimes, CLI IPC/session journals, elevated helper
ownership, independent service paths, installer/signing inventories, accessibility
and release documentation. This is a focused engineering review, not certification
of every Windows service or privilege boundary.

## Automated Evidence / 自动化证据

- Locked restore and Release build: zero warnings/errors.
- Core suite: **104 passed, 0 failed, 0 skipped**, using the existing isolated
  native signature fixture; no production publisher was added.
  Report: `artifacts/verification/v0.8.0/test-results.json`.
- Engineering contracts: stale payload refusal, pinned installer translation,
  full application/CLI/Host signing inventory, missing publisher blocking,
  explicit skip/failure semantics, embedded-pin mismatch and unsigned rejection.
  Report: `artifacts/verification/v0.8.0/engineering-final/results.json`.
  The missing/broken-fixture subruns intentionally skip/fail to test these gates;
  they are not the successful 104-group suite.
- UI: Chinese/compact English, light/dark, health controls, CLI/recovery,
  accessibility names/labels/focus and inline keyboard editing. Loaded/dispatcher
  fatal cleanup leaves no surviving fixture processes.
  Report: `artifacts/verification/v0.8.0/ui-reviewed/summary.json`.
- Incremental P95: 89.411 ms (Chinese), 22.8224 ms (compact English);
  live-log P95: 159/109 ms respectively. Both performance gates pass; these local
  measurements are not real-time guarantees.
- Markdown images: six newly generated v0.8.0 WPF screenshots, visually checked
  and inventoried by version/SHA-256 in [manifest](images/manifest.json).
  [Screenshots](screenshots.md) contain only isolated fixture data; first launch
  stays empty. `tests/Verify-Documentation.ps1` runs in CI.
- Installer tests: current payload and v0.7.1 payload rebuilt with the current
  installer script under independent GUID AppId/mutex, no shortcuts/autostart.
  Both languages check fresh empty data, upgrade configuration/log/backup/portable
  marker hashes, installed file/version equality and uninstall data preservation.
  Reports: `artifacts/verification/v0.8.0/installer-zh/results.json` and
  `artifacts/verification/v0.8.0/installer-en/results.json`.
  The final release asset verification is recorded on the GitHub Release.

## Limits / 尚未验收

- Actual UAC consent, real SCM registration, LocalService permissions, Session 0
  service shutdown and Narrator/NVDA are still manual acceptance checks.
- Physical Windows 10 icon/encoding/installer upgrade and real services remain
  deferred at the user's request. Isolated AppId testing is not a production-user
  upgrade test and does not execute the byte-identical public installer.
- No production certificate/publisher pin: in-app downloads remain disabled and
  trusted publication remains blocked. Test signatures never establish production
  identity. SHA-256 proves integrity only.
- Independent service runtime upgrades/removal remain explicit administrator
  operations. Desktop install/upgrade does not register or replace that service.
- Temporary CI certificate removal is checked by command success; an independent
  post-cleanup certificate-store snapshot is still not implemented.

## 中文摘要

本次修复旧打包目录残留混入、服务相对路径校验和 CLI 身份查询句柄释放。
104 组主回归全部通过；双语界面、日志性能、异常进程清理及图片版本检查通过。
安装验收使用独立身份，不修改现有配置或真实服务，不代表 Win10 实机与正式安装身份验收。
发布仍为未签名预发布，只能手动下载安装。
