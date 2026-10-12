# Test isolation and scale / 测试隔离与规模验证

Included in v0.8.1. Production signing configuration and user service data are unchanged.
v0.8.1 收录的改动，不改变生产签名配置及用户真实服务数据。

## Independent Deadlines / 独立超时

Each core case executes in its own native job-contained test host, with an exact-name filter and a 120-second deadline. The parent drains stdout/stderr concurrently. Completion includes all job descendants, not just the root process. On success, failure or timeout, the parent terminates the job and awaits exit and pipe draining (each cleanup wait is capped at 10 seconds). Killing the isolated host stops its background tasks, rather than merely abandoning a timed-out Task. Missing child reports/provenance and wrong inventories fail the parent case.

每组核心测试在独立宿主执行，精确匹配名称，120 秒超时。宿主在恢复执行前已加入 Windows Job；父进程并行排空输出，等待整个 Job 结束。成功、失败及超时均终止 Job 并等待退出和管道排空，清理等待各有 10 秒上限；不会仅结束等待而留下后台任务。缺失报告、来源信息或清单不匹配均失败。新增故意挂起的根进程与子进程回归，确认超时后两者均不存在。

Evidence subprocesses have 30-second deadlines and explicit process-tree cleanup. The bilingual resource tool has a 90-second job deadline in CI. UI smoke timeout cleanup kills its process tree and waits at most 10 seconds.

证据生成工具限时 30 秒并清理进程树；CI 资源检查限时 90 秒并受 Job 管理。界面冒烟超时后终止进程树，清理等待最多 10 秒。

## XAML Resources / 资源键

The test host parses tracked application XAML as XML and inventories simple StaticResource/DynamicResource keys, including quoted keys. It initializes the real application dictionaries and checks both zh-CN and en-US: referenced keys must resolve, translated strings must be nonempty and present in the English catalog. Missing-key and empty-translation fixtures must be rejected. This is a resource-key gate, not a complete visual, accessibility or code-generated-string audit.

解析应用 XAML，提取静态与动态资源键（含引号键），加载真实应用字典，分别应用中文和英文。引用必须存在，文本不能为空且必须有英文翻译；缺失键和空翻译反例必须被拒绝。这不是完整的视觉、无障碍或代码创建文本覆盖率审查。

## Scale And Soak / 规模与持续运行

The isolated workload saves and reloads 1,000 uniquely identified configurations and verifies IDs/names exactly. It materializes their runtime snapshots but starts **only one synthetic worker**, not 1,000 services. The worker emits 200 MiB of numbered 1 KiB UTF-8 lines containing Chinese and emoji, then emits periodic ticks until graceful stdin stop and an unterminated Unicode tail.

隔离负载保存并读取 1000 个不同 ID 的配置项，核对 ID 与名称，并创建其运行时快照。**只启动一个合成进程，不是 1000 个服务。** 输出 200 MiB、带编号的 1 KiB 中文/emoji UTF-8 行，之后持续输出心跳；通过标准输入停止，最后输出无换行的中文/emoji 尾部。

A separate real-window check loads 1,000 rows, scrolls to the last item and verifies that only 1-100 row containers are realized at each end. It records load/scroll time, memory and handles; no configured target is executed.
另有真实窗口门禁：加载 1000 行并滚动至末项，首尾实际行容器都必须在 1-100 个内，记录加载/滚动时间、内存及句柄；不执行任何配置目标。

Samples record private/managed memory, handles, retained/cache counts and formatting counts every second. Cache/retained limits are 3,000 records. End-versus-midpoint resource-growth gates allow at most 128 MiB private memory and 32 handles. These broad budgets catch gross growth; they are not proof of zero leaks or machine-independent capacity. Export is streamed, with every burst line checked for exact content and ordering, a full stdout hash, zero skipped records and the final tail. Disk history/export are retained under the isolated root named in the report. Reports include source hashes, commit, environment and timestamps.

每秒记录私有/托管内存、句柄、保留记录、缓存及格式化数量。保留及缓存上限为 3000 条；末尾相对中点增长门禁为私有内存不超过 128 MiB、句柄不超过 32。该宽松阈值检测明显增长，不证明零泄漏或跨机器容量。导出按流读取，逐行核对序号和完整内容，记录完整 stdout 哈希、零跳过和停止尾部；原始日志及导出保存在报告所列隔离目录。报告附提交、时间、环境和源码哈希。

```powershell
# Local ten-minute observation; extend to 3600 or 86400 seconds for endurance testing.
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --scale-validation artifacts/verification/scale-and-gates/scale-soak.json 600 200
# Bounded bilingual resource check.
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --bounded-tool 90 tests/LaunchDesk.UiChecks/bin/Release/net10.0-windows/LaunchDesk.UiChecks.exe --resource-keys artifacts/verification/scale-and-gates/resource-keys.json
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --bounded-tool 90 tests/LaunchDesk.UiChecks/bin/Release/net10.0-windows/LaunchDesk.UiChecks.exe --scale-ui artifacts/verification/scale-and-gates/scale-ui.json
```

CI runs a bounded 120-second / 200 MiB scale workload on each verification, alongside the 107-group core inventory and existing UI, log-performance and fatal-cleanup gates. Local ten-minute evidence is separate. A ten-minute run is only a short sustained observation; 1-hour/24-hour endurance and Windows 10 physical-machine acceptance remain separate work. No test signing trust is installed on developer machines.

CI 执行 120 秒 / 200 MiB 规模门禁，以及 107 组核心测试和现有界面、日志性能、异常清理门禁；本地 10 分钟证据单独记录。10 分钟只代表短期持续观测，1 小时/24 小时耐久及 Win10 实机验收仍属独立工作。不在开发电脑安装测试签名信任。

## Local Evidence / 本地证据

2026-10-12, SDK 10.0.401, unpublished working tree based on `efd4bcccf3c8711b2e25c801b053efdfb8eddbf4`. Report and all 109 source hashes were checked against the final working tree. No remote CI run or release is claimed for these changes.
2026-10-12，SDK 10.0.401，基于上述提交的未提交工作区；已核对报告及 109 个源码哈希。本轮未推送，未宣称远端 CI 或发布完成。

- Core: 104 passed, 0 failed, 3 explicitly skipped signing-fixture cases (107 total), `artifacts/verification/scale-and-gates/core-final-scope.json`.
- Resources: 2 XAML files, 57 keys, both languages and both negative fixtures passed, `resource-keys-complete.json` in that directory.
- Real window: 1,000 items, 5/4 realized row containers at the two ends, `scale-ui.json`.
- Sustained capture: 600 requested seconds, 610.27 seconds including stop/export verification, 592 samples, 209,715,200 payload bytes, 204,800 exact burst lines and 593 exact ticks; tail drained, zero skipped records. `scale-soak-final.json` and its metadata are the passing long-run evidence.
- Private memory: 63.28-87.57 MiB during capture; midpoint-to-end growth 544,768 bytes; handles +12; maximum projection cache 690 records, below 3,000. These observations do not establish zero leaks.
- UI/fatal cleanup, backend performance, engineering contracts, documentation and warning-as-error build passed. No test hosts/workers remained after completion.

本地：核心 104 通过 / 0 失败 / 3 跳过；资源与真实窗口门禁通过。持续采集 600 秒，连同停止及导出核对总计 610.27 秒，592 次采样；200 MiB 正文、204800 行及 593 条心跳完整，尾部排空、零跳过。私有内存后半程增长 544768 字节，句柄增加 12，缓存最多 690 条。相关门禁和构建通过，测试进程无残留；这些数据不证明零泄漏。

An earlier long workload reached the export verifier but failed because the **test marker parser** assumed LF for Console.WriteLine, which uses CRLF on Windows. It was not counted as passing. The corrected verifier first passed a separate 30-second / 200 MiB workload, then the full ten-minute workload above. Burst lines are still compared byte-for-byte; only console marker/tick newline handling accepts LF or CRLF.
早期长负载在导出核对时因测试器误将 Console.WriteLine 的 CRLF 当作 LF 而失败，未计入通过。修正后先完成独立 30 秒 / 200 MiB 回归，再完成上述 10 分钟验证。正文行仍精确比较，仅结束标记和心跳识别兼容两种换行。
