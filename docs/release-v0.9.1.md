# LaunchDesk v0.9.1

正式发布，未签名，仅手动安装 / Stable, unsigned, manual installation only.

本版修复日志原始字节缓存预算、管理员助手并发信号及迟到回复串扰，清理就绪检测冗余循环；经实际 DLL 核查，将 UI 冒烟实现拆到测试宿主并增加程序集门禁。复现细节见[复核记录](review-cache-broker-smoke.md)，中英文历史见[更新日志](../CHANGELOG.md)。

The release bounds raw log cache payloads without changing complete persisted/exported logs, correlates elevated-helper signal replies, simplifies readiness waiting and moves smoke code out of the production App assembly. See the [review evidence](review-cache-broker-smoke.md) and [English changelog](../CHANGELOG.en.md).

发布前必须完成对应提交的 Windows CI：110 组核心测试零失败、零跳过，隔离签名样本只用于测试；中英文界面、资源键、异常清理、日志性能、1000 项及 200 MiB 规模门禁。安装器采用隔离身份验证首次安装空白、升级保留配置/日志/备份及卸载保留数据。具体提交、CI、资产及哈希以本版 GitHub Release 为准，不以旧报告替代新产物验收。

Release evidence must refer to the exact release commit. The GitHub Release records CI, installer acceptance and final asset hashes; earlier local reports are not release-binary certification.

生产签名发布保持阻断，不使用测试签名替代发行者授权。Win10 实机、真实 UAC/SCM、读屏与一小时耐久测试仍未完成；历史解释器偶发超时根因仍未定位。正式/Latest 标记不代表代码签名认证或这些验收已完成。

Production signing remains blocked. Physical Win10, real UAC/SCM, screen-reader and one-hour endurance acceptance are pending. The historical intermittent interpreter timeout remains unexplained. Stable/Latest status does not imply publisher-signature certification.
