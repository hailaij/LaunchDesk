# N-5: Log display boundary / 日志显示边界

Unreleased repair; baseline commit `c1e7835730d05395e488e944d2f4dea2f6f6a262`.
本轮未更新版本，不涉及生产签名授权。

## Reproduction / 复现

The original projection cuts the beginning of its oldest displayed string when formatting pushes the retained text beyond 500,000 characters. A 499,980-character single record reproduced a broken timestamp/channel prefix. Merged source labels contribute additional overhead.
原实现按字符切掉首行开头。499,980 字符的单条记录即可复现时间及通道前缀损坏；合并来源名称进一步增加格式化长度。

## Repair / 修复

Cached display lines retain their prefix length as metadata instead of parsing the text. Only the oldest visible line's content is shortened; its complete prefix and `[... truncated ...] ` marker are retained, followed by the newest suffix. Cuts do not split UTF-16 surrogate pairs. If the remaining budget cannot hold the prefix and marker, the old line is omitted whole rather than showing a broken label. Unclipped cached text is not mutated, so repeated updates and filters do not accumulate markers.
缓存记录前缀长度，不从日志文本反推。仅裁剪最旧可见行的正文，保留完整来源前缀、截断标记及正文尾部，避免切断 emoji 代理对。若剩余空间连前缀与标记都无法容纳，整行移除；不显示残缺标签，也不修改缓存原文。

Disk records, raw bytes and exports are unchanged. The existing live-view retention of 3,000 records / 500,000 raw text characters is unchanged: a single record above that raw-text limit may be omitted from the live view, but remains complete on disk and in exports. This repair addresses formatting-induced partial-line truncation, not the separate retention policy.
磁盘记录、原始字节及导出不变。保留原有 3,000 条 / 500,000 原文字符的内存限制：单条超出原文上限时可能不进入实时视图，但磁盘及导出仍完整。本次只修复格式化导致的行内裁剪，不改变保留策略。

## Regression / 回归

One new core group (106 total) covers 499,980 / 499,999 / 500,000 characters with Chinese and emoji, individual and merged views, two real source stores, insufficient prefix space, unchanged cached updates, and an oversized 500,001-character disk-only record. Before/after JSONL exports are compared byte-for-byte, including merged source metadata. Existing log performance and fatal-cleanup gates remain enabled.
新增一组核心回归，总计 106 组。覆盖上述三种边界长度、中文与 emoji、单项与合并视图、两个来源、标签空间不足、重复缓存读取和超过内存上限的完整磁盘记录。显示前后 JSONL 导出逐字节一致，包括合并来源信息；保留性能及异常清理门禁。

```powershell
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --report artifacts/verification/projection-boundary/focused.json --filter "Log projection boundary"
./tests/Assert-TestReport.ps1 -Path artifacts/verification/projection-boundary/focused.json -ExpectedCount 1 -RequireNoSkips
```

Local baseline failure is retained in `artifacts/verification/projection-boundary/before.json`; repaired evidence is generated under the same isolated directory. Working-tree evidence is not a published release or a substitute for commit-bound remote CI.
修改前失败报告及修改后证据均保留在该隔离目录；工作区验证不等于已发布版本，也不代替对应提交的远端 CI。
