# LaunchDesk v0.10.1

正式发布，未签名，仅手动安装 / Stable, unsigned, manual installation only.

## Changes / 变更

- 修正 stderr 前缀被统一标为错误红色的问题。部分服务（如 Alist）将 INFO、WARN 写入 stderr；stdout/stderr 均使用中性色，不自动推断严重程度，也不改变真实输出通道。
- 保留警告和错误原文、通道过滤、日志导出、虚拟化、链接及选中行行为。服务状态仍由进程及就绪/健康检测判断。
- 补浅色、深色、高对比度与 Alist 风格日志回归，更新中英文使用说明。

Use neutral stdout/stderr prefixes without conflating output pipes with severity. Alist INFO messages written to stderr no longer appear as errors. Original messages, channels, filters, exports, virtualization, links and selection remain intact. Process and readiness/health checks still determine service status.

## Verification / 验证

发布前要求对应提交的完整 Windows CI 通过；隔离签名样本仅用于测试，不能代替生产授权。安装器以隔离 AppId 验证首次安装无示例、升级保留配置/日志/备份及卸载保留数据。提交、CI 链接和最终资产哈希记录在 GitHub Release。

Release requires passing exact-commit Windows CI and isolated installer acceptance. Test signing fixtures are not production authorization. GitHub Release records the source commit, CI URL and final asset hashes.

Win10 实机、真实 UAC/SCM、读屏及本版一小时耐久未验收；高对比度仅为资源策略模拟。历史 npm/PowerShell 偶发退出超时根因仍未定位。未签名安装包仅手动下载安装；生产 SignPath 链路继续阻断，Latest 不代表发行者签名认证。

Physical Win10, real UAC/SCM, screen-reader and one-hour endurance acceptance of this version remain pending. High contrast is simulated. Historical intermittent npm/PowerShell shutdown timeouts remain unexplained. Packages are unsigned and manual-install only; production SignPath signing remains blocked. Latest is not publisher-signature certification.
