# 日志口径、回归与测试数据策略

本记录保存 v0.9.1（`b411df4`）之后的开发测量，相关改动纳入 v0.10.0。这里的本地历史结果不替代发布提交的远端 CI；正式签名门禁未放宽。发布证据见 [v0.10.0](release-v0.10.0.md)。

## 日志时间与容量

- 文件名和日期轮转统一按同一条记录的 UTC 时刻，不再分别读取本地日期和另一次系统 UTC 时间。记录仍保存本地偏移，界面时间显示保持本地时间。
- 可注入 TimeProvider 的构造重载用于测试；原有三参数构造保留。UTC 午夜、非 UTC 时区的本地午夜和运行中时区偏移变化都用隔离时钟验证，不修改系统时区或时间。
- 覆盖 UTC-12、UTC、UTC+8、UTC+14；跨 UTC 日期必须轮转，本地午夜或时区切换未跨 UTC 日则不轮转。既有历史文件不改名、不重写。
- `MaximumMemoryRecords`、`MaximumMemoryCharacters`、`MaximumMemoryPayloadBytes` 定义保留预算，环形容量派生为条数上限加一。合并和历史加载采用同一预算。

## 引用契约与用例粒度

保留中的 LogRecord 在 Snapshot/ReadSince 之间保持同一对象引用；该契约有注释和回归覆盖。回归检查重复文本、两轮环形回绕、过期游标重置、每次追加只格式化一条、缓存有界及清屏淘汰。

原来一组显示边界测试拆为九组：三个长度分别覆盖单项/合并，两种合并尺寸分布，以及超过字符预算但仍完整落盘导出的记录。总清单从 110 增至 128 组：拆分增加八组，时间及引用五组，临时目录策略五组。增加计数不等于新增同等数量的功能覆盖。

后端性能门禁明确区分分配的 bytes/operation 与 10000 次操作的 ms total。原有时间和分配阈值保持不变，不通过调整单位放宽门禁。

## 先测量，再决定

`--maintenance-benchmark` 以七轮交替顺序对比同步 Flush 后的 UTF-8 字节计数和 BaseStream.Position，并逐轮核对字节数与文件 SHA-256 一致；另对同一完整源码清单比较 ReadAllBytes 与流式 SHA-256。它是局部预热基准，不代表跨机器性能、断电耐久或零泄漏。

| 对比 | 首轮中位数 | 第二轮中位数 | 决定 |
|---|---|---|---|
| 2000 条写入 + UTF-8 字节计数 | 9.74 ms | 10.06 ms | 暂保留生产实现 |
| 2000 条写入 + 流位置计数 | 9.48 ms | 8.93 ms | 收益幅度波动，未据此更改写入与 Flush |
| 全量源码 ReadAllBytes 哈希 | 7.93 ms / 745936 B（114 文件） | 5.80 ms / 759632 B（117 文件） | 改为流读取以减少分配 |
| 同清单流式哈希 | 8.54 ms / 79552 B | 6.37 ms / 77416 B | 完整清单和逐项 SHA-256 不变，耗时未改善 |

两轮源码清单不同，是期间新增测试文件所致；只比较各轮同清单的配对结果。默认仍生成全量源码哈希，包括本地与每个独立测试宿主；不使用缓存、不改为只有 commit/dirty 信息。来源 sidecar 新增 `sourceHashCost`，记录实际哈希阶段的文件数、耗时和分配。

证据：`artifacts/verification/log-policy/baseline-costs.json`、`final-costs.json` 及各自 `.source-hashing.json` 和 `.metadata.json`。

## 临时数据保留

新的核心、后端基准、测量和规模测试目录带独立所有权标记，仅清理当前宿主自己创建的目录：

- 全部执行通过且报告及 sidecar 写在数据目录外时，清理成功的合成数据；报告不会被删。
- 失败、跳过、未完成、缺少证据、默认报告位于数据目录内，或显式 `--keep-test-data` 时保留。核心保留标志传递到独立用例宿主。
- 清理前核对绝对路径、所有权及整棵目录无重解析点；目录联接、链接证据或校验失败均保留并输出原因。联接负样本证明外部哨兵未删除。
- `Get-TestTemporaryData.ps1` 只读盘点，标记 active、legacy-no-owner、retained-or-interrupted、linked-refused 等状态。超过 14 天仅建议人工复核，不自动删除，也不遍历或计算链接目标大小。
- 本机曾盘点到 1265 个无所有权标记的历史核心/基准目录；它们没有被自动清理。新增策略不追认旧目录的所有权。

## 验证命令

```powershell
dotnet build LaunchDesk.slnx -c Release --no-restore -warnaserror
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --report artifacts/verification/log-policy/core-accepted.json
./tests/Assert-TestReport.ps1 -Path artifacts/verification/log-policy/core-accepted.json
./tests/Verify-Ui.ps1 -OutputDirectory artifacts/verification/log-policy/ui
./tests/Verify-Engineering.ps1 -OutputDirectory artifacts/verification/log-policy/engineering
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --maintenance-benchmark artifacts/verification/log-policy/costs.json
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --log-benchmark artifacts/verification/log-policy/backend.json
./tests/Assert-LogBackendPerformance.ps1 -Path artifacts/verification/log-policy/backend.json
./tests/Get-TestTemporaryData.ps1
# Keep successful core fixture data explicitly:
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --report artifacts/verification/kept.json --filter 'Log policy:' --keep-test-data
```

本地最终核心：125 通过、0 失败、3 项签名样本明确跳过；不安装开发机测试信任根。构建、界面、异常清理、工程门禁、后端性能及文档图片门禁通过。早期四个时间用例因测试读取器未允许共享写而失败，已修正测试读取方式，失败报告 `policy.json` 保留且不计入通过。

## 一小时耐久

用户已授权本轮运行，Win10 实机继续暂缓。

```powershell
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --scale-validation artifacts/verification/log-policy/soak-1h.json 3600 200
```

1000 项配置保存/重载，只启动一个合成进程；200 MiB 编号中文/emoji 日志后持续心跳，逐秒采集私有内存、托管内存、句柄和缓存条数。停止后核对完整导出、编号次序及 Unicode 尾部排空，清理进程树。

| 本轮实际结果 | 数值 |
|---|---|
| 状态 | 通过；报告、源码清单哈希及临时数据清理已核对 |
| 请求 / 实际耗时 | 3600 / 3604.724 秒（含停止与导出核对） |
| 采样 | 3551 次 |
| 载荷与编号行 | 209715200 字节 / 204800 行完整匹配 |
| 心跳 | 3564 条连续匹配 |
| 停止尾部 / 跳过记录 | Unicode 尾部排空 / 0 |
| 私有内存范围 | 47.01–92.34 MiB |
| 后半程私有内存净变化 | -3170304 字节（约 -3.02 MiB） |
| 后半程句柄净变化 | +3 |
| 缓存峰值 | 3000 条，未超过上限 |
| 数据与证据 | 成功的合成数据目录已清理；报告和来源 sidecar 保留 |

证据：`artifacts/verification/log-policy/soak-1h.json`、相邻 `.metadata.json`、`soak-1h-validation.json`。原始报告来源为 `b411df4` 加本次未提交源码哈希，不是该发布包或远端 CI 的验收。这是一个合成进程的一小时观测，不证明零泄漏、1000 个并发服务容量、24 小时耐久或 Win10/真实服务验收。

## English Summary

Rotation and filenames now share the same captured UTC instant; records retain local offsets. Retention limits derive the ring capacity. New regressions cover timezone boundaries, stable reference identity and safe temporary-data retention, while boundary cases are separately named. Core inventory is 128 groups (125 passed locally, three signing-fixture skips).

Paired measurements did not establish a stable reason to change production byte counting. Full source hashing now streams files, reducing allocation while retaining the complete inventory and identical digests; hashing was not faster. Performance thresholds are unchanged and units explicit.

Successful owned fixtures with external evidence are cleaned; incomplete, explicitly retained, linked or unverified data is preserved. Legacy folders are inventoried only. The one-hour synthetic soak passed in 3604.724 seconds: 204800 exact burst lines, 3564 consecutive heartbeats, complete Unicode tail draining and zero skipped records. Cache peak was 3000; second-half private-memory change was -3.02 MiB and handle change +3. Successful fixture data was removed, with reports retained. This is not proof of zero leaks or real-service capacity. Physical Windows 10 and real UAC/SCM acceptance remain pending. No version bump, push or release occurred.
