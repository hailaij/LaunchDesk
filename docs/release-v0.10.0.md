# LaunchDesk v0.10.0

正式发布，未签名，仅手动安装 / Stable, unsigned, manual installation only.

## Changes / 变更

- 精简工具栏，增加按选择状态启用的“更多操作”；强化分组层级、成员数与行操作反馈，修正末尾按钮裁切。
- 启动项配置改为五个分类标签，验证错误自动定位，关闭未保存修改前确认；中英文紧凑窗口保留固定操作底栏。
- 显示清理警告，保留会话内列宽，数字验证明确字段及范围，统一强制退出码展示。
- 通道前缀着色，保留字符串投影兼容、虚拟化、选择、链接及高对比度资源策略；不推断正文日志级别。
- 日志轮转与文件名统一 UTC，保留本地记录偏移；统一内存保留常量，补午夜、时区、引用及截断边界回归。
- 补测试临时目录所有权与保留策略，源码哈希使用完整清单的流读取，性能门禁单位明确而不放宽阈值。

The release simplifies frequent workflows, organizes configuration into five tabs, preserves column resizing, exposes maintenance warnings and improves field validation. Log channel styling keeps existing projection/export contracts and virtualization. UTC rotation, retention constants, reference/boundary regressions and owned test-workspace cleanup improve reliability without reducing test evidence.

## Acceptance / 验收

发布须取得对应提交的 Windows CI：128 组核心回归零失败、零跳过；隔离签名样本仅用于测试。包括双语界面与资源键、异常清理、日志性能、1000 项配置/列表及 200 MiB 日志的两分钟规模验证。安装器使用隔离 AppId 验证首次安装无示例、升级保留配置/日志/备份及卸载保留数据。实际提交、CI、资产 SHA-256 和安装验收结果以 GitHub Release 为准。

Release acceptance requires exact-commit Windows CI with 128 passing groups and no skips, plus UI, resource-key, cleanup, performance and bounded scale checks. Isolated installer acceptance verifies empty first install and preservation of configuration, logs and backups. Refer to the GitHub Release for actual provenance, hashes and results.

此前开发源码的一小时耐久已完成，但它不是本版发布源码的一小时验收。Win10 实机、真实 UAC/SCM 与读屏验收仍待完成；高对比度为资源策略模拟。历史 npm/PowerShell 偶发退出超时根因尚未定位，后续通过不等于已修复。

The prior development source completed a one-hour soak; it is not a one-hour acceptance of this release source. Physical Win10, real UAC/SCM and screen-reader acceptance remain pending. High contrast is a resource-policy simulation. Historical intermittent npm/PowerShell shutdown timeouts remain unexplained.

生产 SignPath 签名链路继续阻断，不使用测试证书替代生产授权。程序检查更新后只提供 Release 页面链接，不下载或静默安装。Latest 标记不代表发行者签名认证。

Production SignPath signing remains blocked; fixture certificates are never production authorization. Update checks only offer a release-page link, without in-app downloads or silent installation. Latest does not imply publisher-signature certification.

[界面验收 / UI notes](ui-usability.md) · [日志策略 / Log policy](log-policy-maintenance.md) · [中文更新日志](../CHANGELOG.md) · [English changelog](../CHANGELOG.en.md)
