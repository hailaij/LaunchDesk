# Log backend and minor review (unreleased)

## Scope / 范围

This is a working-tree verification, not a new release or production signing approval.
本轮为工作区验证，不更新版本，不授予生产签名信任。Windows 10 实机及手动屏幕阅读器验收仍暂缓。

## Minor findings / 小问题

- Recovery: reproduced an enabled restore button after deselecting all candidates. The button now follows the selection, and the click handler refuses empty selections. Regression covers initial selection, deselection, re-selection and no candidates.
- 恢复窗口：已复现全部取消勾选后仍可恢复。按钮随选择更新，点击处理也拒绝空选择；覆盖初始选择、全部取消、重新选择和无候选项。
- Accessibility: Chinese and English automation-peer names, keyboard navigation and focus checks passed. Existing icon action names were retained; this does not substitute for a manual screen-reader or Windows 10 test.
- 无障碍：中英文自动化名称、键盘导航及焦点检查通过，保留现有图标动作名称，不代替手动读屏或 Win10 实机验收。
- Compiler resolution: unsigned builds, signed compilation, CI toolchain checks and isolated installer tests share one resolver. Explicit paths must be files named ISCC.exe; otherwise standard locations, uninstall registration and PATH are considered. No machine-specific compiler path is committed. Reviewed compiler hash and native signature checks remain in Verify-CiToolchain.ps1.
- 编译器：统一路径查找，支持显式文件、标准位置、安装注册项和 PATH；目录、缺失文件和错误文件名被拒绝。未硬编码本机路径，CI 的编译器哈希及验签检查保留。
- Health thresholds: existing failure/recovery threshold tests passed; no semantic change made.
- 健康阈值：现有失败、恢复阈值测试通过，未改变语义。
- `ignoreversion`: retained for application-private payload only. [Inno documentation](https://jrsoftware.org/ishelp/topic_filessection.htm) explicitly limits this flag to private application files. Isolated same-version replacement checks compare every installed payload hash and preserve data/log/backup sentinels; they do not establish a downgrade-prevention policy.
- `ignoreversion`：仅用于应用私有负载，暂不删除；隔离同版本覆盖检查逐文件核对哈希，并验证配置、日志及备份保留。该检查不代表已有防降级策略。

## Measurement / 测量

Warmed single-thread microbenchmarks on this Windows workstation, 10,000 operations per metric:
本机 Windows 单线程预热微基准，每项 1 万次操作：

| Metric | Before | After |
| --- | ---: | ---: |
| Single-record delta / 单条增量读取 | 144.74 ms | 0.47 ms |
| Persist records / 日志写入（仍逐条 Flush） | 442.55 ms | 32.61 ms |
| Explicit UTF-8 allocation / 固定 UTF-8 分配 | 312,560,088 bytes | 88,240,088 bytes |
| Auto decode allocation / 自动解码分配 | 144,480,088 bytes | 88,240,088 bytes |
| Raw writer, flush each / 原始写入逐条 Flush | 20.20 ms | 19.26 ms |
| Raw writer, buffered loop / 原始缓冲写入循环 | 0.57 ms | 0.51 ms |

Decoder latency was not consistently improved; the decoder change targets allocation pressure.
解码耗时未稳定改善，本次解码优化针对分配压力。结果不能推广为所有设备、所有日志负载的加速倍数。
The raw buffered-writer loop flushes after the measured loop; its table timing excludes that final flush. It is an exploratory comparison, not a production persistence/durability equivalence.
原始缓冲写入实验在计时循环后执行最终 Flush，表中循环时间不包含该最终 Flush，不用于证明生产持久化或耐久性等价。

## Implementation / 实现

- Replace retained-record prefix scanning with an indexed bounded ring. Delta copying is proportional to the returned records; retention and reset cursors remain unchanged.
- 有界环形缓冲按索引复制增量，避免遍历保留记录的前缀；保留裁剪及重置游标行为。
- Decode directly from the input span when possible; pool byte/character scratch buffers and return them in finally. Keep incomplete UTF-8 suffixes and raw output bytes.
- 可直接解码时不复制输入，复用字节及字符暂存缓冲并在 finally 归还；保留不完整 UTF-8 尾部与原始输出字节。
- Cache the active log file's local date instead of querying file metadata per record; date changes still trigger rotation.
- 记录活动日志文件的本地日期，避免逐条文件元数据查询，跨日仍轮转。
- Keep synchronous Flush, export snapshot flush and writer disposal. No timer/asynchronous write queue, loss window or new power-loss durability guarantee is introduced. Disk failures still propagate to the process supervisor.
- 保留同步 Flush、导出快照刷新及释放时关闭写入器。不新增定时写队列、丢失窗口或断电耐久性承诺；磁盘错误继续传播至进程监管。

## Regression and gates / 回归与门禁

The existing 105-group core inventory is unchanged. Expanded cases cover ring wrap ordering and immediate stop while producing 12,000 stdout and 12,000 stderr lines, split UTF-8 and an unterminated tail. Expected text and raw bytes are compared, not just line counts. Disk failure and fatal loaded/dispatcher cleanup regressions are retained.
核心测试仍为 105 组；扩充环形缓冲绕回顺序、持续输出期间立即停止、双通道各 12,000 行、UTF-8 分片及无换行尾部，逐字比较文本与原始字节，保留磁盘失败和致命异常清理回归。

```powershell
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --log-benchmark artifacts/verification/log-backend.json
./tests/Assert-LogBackendPerformance.ps1 -Path artifacts/verification/log-backend.json
./tests/Verify-InnoCompiler.ps1
./tests/Verify-Ui.ps1
```

The CI orchestrator runs the new backend allocation/latency gate and compiler-path contracts. Allocation budgets catch structural regressions; timing budgets are intentionally broad for shared runners. Each benchmark exports and verifies all 10,001 persisted records, with a provenance sidecar. The old implementation's explicit UTF-8 allocation fails the new gate.
CI 编排已接入底层日志分配、耗时及编译器路径门禁；分配阈值识别结构性退化，耗时阈值为共享运行器留出余量。基准导出并验证全部 10,001 条持久化记录，附来源元数据。修改前固定 UTF-8 分配会被新门禁拒绝。

Evidence is generated under `artifacts/verification/log-backend/` (ignored by Git), including before/after benchmarks, core reports, UI/fatal-cleanup results, compiler resolution and isolated installer results. Reports distinguish skipped signing fixtures from passed tests. These working-tree changes require a subsequent push before remote CI can verify them.
证据位于上述忽略目录，覆盖修改前后基准、核心报告、界面与异常清理、编译器及隔离安装结果。签名样本跳过不计为通过；本次改动尚需后续推送才能运行远端 CI。
