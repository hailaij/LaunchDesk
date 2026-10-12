# v0.8.1 / 未签名预发布 / Unsigned Prerelease

2026-10-12. **未签名、手动安装 / Unsigned, manual installation only.**

## Fixes And Improvements / 修复与优化

- Manager-switch rollback, isolated service diagnostics and CLI JSON contracts. Fault injection lives in the test host, not the production application.
- Batched log persistence, pooled decoding and indexed incremental reads. Long display lines retain source prefixes, truncation markers and Unicode boundaries; original logs and export remain unchanged.
- Empty recovery selection and installer compiler resolution regressions.
- 107 independently job-contained core cases with 120-second deadlines and process-tree cleanup; bilingual XAML resource gates, 1,000-item configuration/UI and 200 MiB log validation.

管理器切换、服务诊断、CLI JSON、生产故障注入隔离及恢复空选择等修复；优化日志底层与增量读取，超长显示不破坏来源前缀和 Unicode 边界。补齐测试超时清理、资源键和规模门禁。

## Evidence / 证据

The pre-version source commit `de36125f8f3e43c9362234498edccc2b87df8845` passed [CI 38253336441](https://github.com/hailaij/LaunchDesk/actions/runs/38253336441): 107 passed, zero failed or skipped; 57 resource keys in both languages; 1,000 rows with 5/4 realized containers; 200 MiB payload with 204,800 exact lines, stop-tail draining and zero skipped records. Both fatal-cleanup probes left no descendants. This is baseline evidence, not proof of the version-bumped release binary; release-specific CI and asset verification are linked on its GitHub Release.

Local ten-minute observations and limits are documented in [test isolation and scale](scale-and-test-gates.md). Release installation acceptance uses the current script and release payload with an independent AppId/mutex and no shortcuts: empty first use, preserved configuration/log/backup/portable-marker hashes after upgrading the v0.8.0 payload, and data retained after uninstall. This does not execute the production-AppId installer or test historical installer-script migration. Final results and asset hashes are recorded on the Release.

版本升级前基线 CI 为 107/0/0；新版本自身 CI、安装验收和资产哈希见 Release，不能用基线替代新二进制验收。安装测试使用独立身份，不改动现有安装及真实服务。

## Limits / 边界

- No production signing authorization or publisher pin. Trusted publication and in-app download remain blocked; test signatures are never production trust. SHA-256 is an integrity check, not publisher identity.
- One-hour/24-hour endurance tests are deferred; the CI workload is only 120 seconds.
- Physical Win10, real services, UAC/SCM and screen-reader acceptance remain separate manual work. No silent installation or automatic service-runtime replacement.

未签名、仅手动安装；1 小时耐久本轮不执行，Win10 实机等验收继续暂缓。
