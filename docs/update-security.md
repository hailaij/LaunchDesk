# 更新边界 / Update Boundaries

[中文 README](../README.md) | [English README](../README.en.md)

## v0.9.0 当前行为

保留可关闭的启动后台检查与手动检查，仅从固定仓库获取最新正式 Release 的版本、说明；不提示预发布。发现新版本时显示提示，用户点击“打开下载页”后打开默认浏览器中的对应 GitHub Release。程序不自动打开浏览器，不下载、验签或安装更新包。

版本标签严格校验，页面地址从固定仓库生成，不采用远端提供的页面/资产链接。网络或解析错误不替换程序、配置或服务。下载与安装由用户在浏览器中手动完成；SHA-256 校验不是发行者身份证明。

## 历史与发行签名

v0.6.0 至 v0.8.1 的代码保留在 dev 分支；已发布标签与 Release 不变。这些旧版包含受严格签名配置约束的下载功能，不能将本文当作旧版行为说明。旧版验证报告继续保留。

正式签名发行工具及 [签名发布门禁](trust-and-engineering.md) 独立保留，缺少生产授权、证书或发行者 pin 时阻断可信发布。移除程序更新入口不授权测试证书进入生产信任。当前安装包仍为未签名预发布、仅手动安装。

## English

v0.9.0 retains configurable startup and manual checks for newer stable releases. Checks fetch metadata only. Clicking Open download page opens the corresponding fixed GitHub Release URL in the default browser. The app never automatically opens a browser, downloads or verifies installers, or runs an installation.

Strict tag validation and a hard-coded repository prevent remote metadata URLs from choosing the destination. Browser downloads and installation are manual. Hashes do not prove publisher identity. Versions through v0.8.1 remain on dev and their tags/Releases are unchanged; their historical download behavior differs.

Production signing tools and release gates remain independently enforced. Missing authorization, certificate or publisher pin blocks trusted publication; test certificates are not production trust.
