# 日志缓存、管理员助手与冒烟边界复核

日期：2026-10-14。基线为 `d54a572dd88b9a3d422d8251f15068a888c0d1c9`，本记录描述未提交的本地修复，不代表发布包或远端 CI 已验收。

## 逐项复现

1. **原始日志缓存没有字节预算。** 向隔离 LogStore 写入 3000 条短文本，每条附带不同的 8192 字节原始数据。旧实现仅限制条数和文本字符数，快照载荷估算达到 24,621,780 字节。新增回归在修复前失败。
2. **并发信号覆盖及迟到回复串扰。** 隔离测试宿主以非管理员身份模拟助手，仍使用真实管道协议和受管进程。两次重叠 SignalAsync 导致第一请求超时；另一用例让第一回复超时后携带错误返回，旧实现错误地使第二请求失败。两个新增回归在修复前失败。没有弹出 UAC、修改服务或更改生产管理员校验。

基线失败报告位于 `artifacts/organization/review-baseline-tests.json`，相邻 `.metadata.json` 记录提交、环境、时间及源码哈希。基线失败是故意保留的复现证据，不能计为通过。

## 修复与覆盖

- LogStore 写入、历史加载和合并快照同时限制 3000 条、500000 字符和 4 MiB 载荷，保留最新尾部。载荷计算为 UTF-16 文本长度乘 2 加 RawBytes 长度，不包括对象开销、序列化临时分配或调用方持有的快照；也不是所有启动项合计的全局预算。
- 原始数据仍完整落盘和导出；单条超过预算时不进入缓存。回归覆盖 3000 条原始数据逐条导出校验、历史加载、清屏计数重置、合并预算、淘汰后的增量游标重置和超大记录导出。
- 管道信号增加独立 RequestId，回复仅完成对应请求；超时后移除登记，迟到回复不能完成后续请求；助手失败或退出时失败所有待处理请求。
- 报告第二条建议中的 ReadinessMonitor.WaitAsync 外层循环经检查只执行一次，不是运行故障。移除冗余循环，保留 ProbeAsync 的实际重试及提前退出、取消和超时处理，由现有就绪检测回归覆盖。

## 核查实际生产程序集

并非仅凭同文件判定。对保留的 v0.9.0 发布 ZIP 内 `LaunchDesk.dll` 做 PE 元数据检查，实际存在 `smokePath`、`Capture`、`VerifyVirtualLogAsync`、`VerifyIncrementalLogAsync`，SHA-256：

`813966F59AE4B0CDCB2F2EB6588EFAFDDB837A280EC13F9876806F1182DA2A76`

但生产入口拒绝 `--ui-*` 参数，正常构造窗口时不启用冒烟路径。因此这证明生产程序集含测试代码，**不证明生产测试参数可执行，也不构成远程漏洞结论**。

现将截图、示例数据、虚拟化及增量性能冒烟迁入 `tests/LaunchDesk.UiChecks/UiSmokeRunner.cs`；测试宿主使用真实 MainWindow 实例，继续调用生产的刷新和异常清理逻辑。生产窗口不再接受冒烟路径，仅保留默认显示原有错误对话框的内部通知接口；测试宿主通过该接口避免阻塞式对话框。测试项目不作为生产程序集引用。

增强 `--production-boundary` 门禁：生产 App 必须不含 FaultProbe、UiSmokeRunner 或上述 MainWindow 冒烟成员；UiChecks 负样本和仅含冒烟方法的 Core 测试程序集负样本必须被拒绝。`Verify-Engineering.ps1` 已接入该检查。新构建 App 的冒烟成员清单为空。

## 验证及范围

```powershell
dotnet build LaunchDesk.slnx -c Release --no-restore -warnaserror
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --report artifacts/verification/review-core-final.json
./tests/Assert-TestReport.ps1 -Path artifacts/verification/review-core-final.json
./tests/Verify-Ui.ps1 -OutputDirectory artifacts/verification/review-ui-accepted
./tests/Verify-Engineering.ps1 -OutputDirectory artifacts/verification/review-engineering-final
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --log-benchmark artifacts/verification/review-log-backend.json
./tests/Assert-LogBackendPerformance.ps1 -Path artifacts/verification/review-log-backend.json
```

构建零警告、零错误。核心清单增加至 110 组，107 通过、0 失败、3 项签名样本明确跳过，严格 CI 仍拒绝跳过。中英文界面、虚拟化、增量性能、恢复、无障碍、服务诊断及两类致命异常清理已通过；异常清理没有遗留子进程。10 项工程门禁通过，其中故意损坏签名样本的内部失败是门禁预期的负样本，不是总体测试失败。日志底层性能门禁通过，10001 条导出无丢失。

没有安装测试信任根、申请生产签名或解除正式签名发布阻断；没有更新版本、推送或发布。远端 CI、真实 UAC 和 Windows 10 实机验收不包含在本次结果中。

## English Summary

Three regressions first failed against the baseline: unbounded raw log payloads, overwritten concurrent signal replies, and a late reply poisoning a later request. Cache payloads now have a per-store 4 MiB budget, preserving full persisted/exported records. Broker replies are correlated by request ID.

ReadinessMonitor.WaitAsync also had a redundant single-iteration loop, not a functional defect. It was removed without changing the actual retry loop inside ProbeAsync; existing readiness regressions cover retrying, timeout, cancellation and early exit.

PE metadata confirmed smoke methods in the old released App assembly; normal production argument handling did not expose those test modes. Smoke implementation now resides only in the UI test host. Production assembly checks reject both test-host types and smoke-method negative fixtures. Local verification is distinct from release or cloud CI acceptance; three signing-fixture groups remain explicitly skipped and production signing remains blocked.
