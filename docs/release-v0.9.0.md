# v0.9.0 / 正式发布，未签名 / Stable Release (Unsigned)

2026-10-13. 未签名、手动安装 / Unsigned, manual installation only.

## Changes / 变更

- `dev` preserves the complete v0.6.0-v0.8.1 history at commit `472c835dad6ad8c69873db4c724c00de3e77261a`; existing tags and Releases remain unchanged. `main` continues from v0.8.1.
- Removed in-app installer downloading and download verification. Startup/manual checks still discover stable releases; a user click opens the fixed GitHub release page. No automatic browser launch or installation.
- Regression cases cover malicious remote URLs, unsafe tags, absence of installer metadata and the removed downloader. Bilingual documentation and privacy policy describe the new behavior.

dev 保留历史，main 基于 v0.8.1 修改；程序更新仅检查版本并由用户点击打开下载页。源码、安装验收及最终资产哈希见对应 Release 的完整验证记录。

## Limits / 边界

The original intermittent npm/PowerShell exit timeout remains unexplained. Passing later diagnostics does not prove a root-cause fix. Production signing remains independently blocked without authorization; test certificates are not production trust. One-hour endurance, physical Windows 10, real services, UAC/SCM and screen-reader acceptance remain deferred.

原解释器超时未定位风险保留；正式签名链路不放宽。1 小时耐久、Win10 实机等验收暂缓。

## Release Status / 发布状态

2026-10-13: v0.9.0 promoted to stable and Latest without changing its tag, binary assets or hashes. Historical titles are normalized to `LaunchDesk v<version>`; their prerelease flags stay unchanged. The released binary remains built from `e4e7ccbf36425d18db2eb0f3ccc0951dbe0b6bcd`; this documentation-only follow-up does not replace the package. Stable status is not a code signature or production signing authorization.

v0.9.0 设为正式 Release 和 Latest，保留原安装包；旧版本仅统一标题，历史预发布标记不变。正式发布不等于代码签名授权。
