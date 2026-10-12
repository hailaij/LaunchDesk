# Privacy Policy / 隐私政策

v0.8.0 additions / v0.8.0 新增功能：用户明确启用的提权启动通过本地认证管道向 UAC 助手传递所选配置和原始进程输入输出，不保存密码、不上传。独立 Windows 服务将所选配置复制到受保护的 ProgramData 目录，日志和备份独立保存，卸载服务后仍保留。Explicit elevation uses authenticated local pipes without storing passwords or uploading data. Independent Windows service configuration, logs and backups are stored in protected ProgramData and retained after service uninstall.

Last updated / 最后更新：2026-10-12

Project / 项目：[LaunchDesk](https://github.com/hailaij/LaunchDesk)

## English

### Scope and Release Status

This policy covers the LaunchDesk Windows desktop application maintained by hailaij and project contributors. It does not cover GitHub's website, other linked websites, or the scripts and applications you choose to launch.

Version v0.6.0 is an unsigned prerelease with local error diagnostics and retention maintenance. Background startup update checks, custom data locations, readiness checks and user-initiated verified downloads were added in v0.4.0. No publisher certificate is configured yet, so in-app downloads are disabled; the release page remains accessible. Older v0.3.0 packages only check from the About window.

### Local Data

LaunchDesk does not require an application account and does not implement advertising, usage analytics, or automatic crash-report uploads. It stores the following data locally to provide its functionality:

- Launch-item names, groups, file paths, commands, arguments, working directories, environment variables, dependencies, and preferences.
- Development crash recovery stores active item GUIDs and a dirty-session flag in the data directory's `session.json`. CLI control uses a local, current-user/login-session named pipe; commands and status are not sent to a remote service.
- Captured process output and error logs, system messages, and input records. These may contain personal information, credentials, or other sensitive content produced by your commands.
- Configuration backups, exported files, and, downloaded update packages.
- Local exception diagnostics under `diagnostics/errors.log`, including error messages and stack traces that may contain private paths. Approximately 2 MiB per file, with up to three rotated files; no automatic upload.

The default data directory is `%LOCALAPPDATA%\LaunchDesk`. Portable mode uses the `data` directory next to the application. The application also supports a user-selected data directory and stores its location in `%LOCALAPPDATA%\LaunchDesk-location.json`. Update downloads use `%LOCALAPPDATA%\LaunchDesk-updates`.

Configuration and log files are not encrypted by LaunchDesk. Access depends on your Windows account, filesystem permissions, device security, and any backup or synchronization tools you use. Choosing a network or synchronized directory may expose files to the services you configured.

### Network Connections

LaunchDesk does not upload your launch configuration, environment variables, captured logs, or process input to the project maintainers as part of update checking or downloading.

- **Manual update checks:** Clicking Check for Updates requests public release metadata from `api.github.com`. Requests include a LaunchDesk User-Agent identifier. GitHub can receive ordinary connection information, such as your public IP address and request time. This check does not require a GitHub account or access token.
- **Startup checks:** A background request to the same GitHub endpoint runs at startup when the startup-update preference is enabled. This preference is enabled by default and can be disabled in Settings. No installation or package download starts merely because an update check succeeds.
- **Update downloads:** Clicking the download button requests the installer from the project's GitHub Releases URL and approved GitHub asset domains. Downloading requires a configured trusted publisher and suitable release metadata. Windows signature validation may contact certificate-chain or revocation services operated by certificate authorities or Windows trust infrastructure. Downloads are not silently installed.
- **User-configured readiness checks, in v0.4.0:** TCP or HTTP checks connect to the host or URL you configure, which may be local or remote. The destination receives normal connection/request information. A log-keyword check does not itself make network requests.
- **Health monitoring (in development):** Monitoring is disabled by default for each item. When enabled, it repeatedly connects to your configured TCP host or sends HTTP GET requests to your configured URL after the item is ready. The destination receives ordinary connection/request information; results and state changes are stored locally. You can disable monitoring per item. This does not upload data to project maintainers.
- **Opening links:** Repository, feedback, release, and log links open in your browser only after your action. For log links, Ctrl+click is required. Your browser and the destination website handle their own cookies, accounts, and network data. LaunchDesk does not prefetch log links.

GitHub's handling of its own service data is described in the [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). Other destinations and certificate services have their own policies.

### Programs You Launch

Scripts, npm commands, and executable programs launched through LaunchDesk may independently access files, communicate over the network, collect information, or send telemetry. Their behavior and privacy policies are separate from LaunchDesk. Only launch trusted resources, and review their configuration before use.

### Retention and Your Controls

Log retention defaults to seven days and approximately 200 MiB per item. Cleanup runs during rotation, at startup and hourly, including inactive items. These limits can be changed in Settings. Clearing the visible log panel does not delete saved log history.

You can delete saved log history with the application's confirmation flow, export configuration and logs, and remove local files yourself after exiting the application. Configuration is retained until replaced or deleted. Automatic configuration backups retain the most recent 20 files; manual backups are kept separately and do not expire through that limit. Exports and verified update downloads remain until you remove them. Uninstalling preserves user configuration, logs, backups, and related local data; it is not a data-erasure operation.

changing the data directory copies configuration, logs, and backups to a new empty destination and preserves the original directory. A failed migration may leave a recoverable staging directory. A failed or cancelled update download attempts to remove its temporary package and never replaces your running application or configuration. Forced termination or a filesystem error may leave temporary files requiring manual removal.

### Public Support and Policy Changes

Support is available through [GitHub Issues](https://github.com/hailaij/LaunchDesk/issues). Issues and attachments may be public. Do not post credentials, private paths, unredacted logs, or personal information. Project maintainers can see information you voluntarily submit there; GitHub manages the hosting and account data.

Changes to this policy are published in this repository with an updated date. Questions about LaunchDesk's data handling can be raised through the project's issue tracker without including sensitive information.

## 中文

### 适用范围与版本状态

本政策适用于由 hailaij 及项目贡献者维护的 LaunchDesk Windows 桌面应用，不适用于 GitHub 网站、其他链接网站，以及用户自行启动的脚本或程序。

v0.6.0 为未签名预发布，增加本地异常诊断及日志保留维护。v0.4.0 起支持启动时后台更新检查、自定义数据目录、就绪检测及用户主动下载与签名验证。当前尚未配置发行证书，应用内下载禁用，但可打开发布页面。旧 v0.3.0 安装包仅在关于窗口主动检查。

### 本地数据

LaunchDesk 无需应用账号，不提供广告、使用统计或自动崩溃报告上传。为实现功能，应用在本地保存：

- 启动项名称、分组、路径、命令、参数、工作目录、环境变量、依赖和偏好设置。
- 崩溃恢复在数据目录的 `session.json` 保存运行项 GUID 及异常退出标记；CLI 控制使用当前用户、当前登录会话内的本地命名管道，不向远程服务发送命令或状态。
- 进程标准输出、错误日志、系统消息及输入记录；这些内容可能包含脚本产生的个人信息、凭据或其他敏感内容。
- 配置备份、导出文件，以及更新下载包。
- `diagnostics/errors.log` 中的本地异常诊断，包括错误及调用栈，可能含私人路径。每份约 2 MiB，另保留最多 3 份轮转文件，不自动上传。

默认数据目录为 `%LOCALAPPDATA%\LaunchDesk`，便携模式使用程序旁的 `data` 目录。本支持自定义数据目录，其位置记录保存在 `%LOCALAPPDATA%\LaunchDesk-location.json`；更新下载使用 `%LOCALAPPDATA%\LaunchDesk-updates`。

LaunchDesk 不对配置和日志进行加密。文件访问由 Windows 账号、文件权限、设备安全及用户使用的备份或同步工具决定。选择网络或同步目录时，相关服务可能访问这些文件。

### 网络访问

更新检查和下载不会向项目维护者上传启动配置、环境变量、捕获的日志或进程输入。

- **正式版 v0.3.0 更新检查：** 用户点击检查后，向 `api.github.com` 请求公开发布信息，并发送 LaunchDesk User-Agent 标识。GitHub 可收到公网 IP、请求时间等正常连接信息，无需 GitHub 账号或访问令牌。
- **启动检查：** 启动更新检查设置开启时，应用在启动后向相同 GitHub 接口发送后台请求；此设置默认开启，可在设置中关闭。检查成功不会直接触发下载安装。
- **更新下载：** 用户点击下载后，从项目 GitHub Releases 地址及允许的 GitHub 资产域名请求安装包。下载需具备受信任发行者配置和合适的发布信息。Windows 验签可能访问证书机构或 Windows 信任基础设施的证书链、吊销查询服务。不静默安装。
- **就绪检测：** TCP 或 HTTP 检测访问用户配置的主机或地址，可能为本地或远程目标；目标可收到正常连接或请求信息。日志关键词检测本身不产生网络请求。
- **健康监控：** 每个启动项默认关闭监控。启用后，启动项就绪时开始定期连接用户配置的 TCP 主机或向配置的 URL 发送 HTTP GET 请求；目标可收到正常连接或请求信息。检测结果及状态变化保存在本地，可按项关闭监控，不向项目维护者上传数据。
- **打开链接：** 用户操作后，仓库、反馈、发布页面及日志链接才会在浏览器打开；日志链接要求 Ctrl+点击，不预先访问。浏览器和目标网站自行处理其 Cookie、账号及网络数据。

GitHub 自身的数据处理见 [GitHub 隐私声明](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement)。其他网站及证书服务适用各自的隐私政策。

### 用户启动的程序

通过 LaunchDesk 启动的脚本、npm 命令或可执行程序，可能自行访问文件、联网、收集信息或发送遥测；这些行为及其隐私政策独立于 LaunchDesk。请仅启动可信资源，并检查其配置。

### 保留期限与用户控制

日志默认每项保留七天、最多约 200 MiB，在轮转、应用启动及每小时后台维护时清理，包括未运行项。可在设置中调整。清空显示面板不会删除已保存的历史日志。

用户可以通过确认操作删除历史日志、导出配置或日志，也可退出程序后自行删除本地文件。配置保留至被替换或删除；自动配置备份保留最近 20 份，手动备份单独保存，不受此数量限制。导出文件及验证通过的更新包保留至用户删除。卸载会保留配置、日志、备份及相关本地数据，不等于清除用户数据。

切换数据目录会将配置、日志与备份复制至新的空目录，并保留原目录；迁移失败可能留下可恢复的暂存目录。下载或验证失败、取消时会尝试清理临时包，不替换当前程序或配置。强制结束程序或文件系统错误可能留下需手动清理的临时文件。

### 公开反馈与政策更新

通过 [GitHub Issues](https://github.com/hailaij/LaunchDesk/issues) 提供支持。反馈和附件可能公开，请勿提交凭据、私人路径、未经脱敏的日志或个人信息。维护者可看到用户自愿提交的信息，GitHub 负责其网站托管和账号数据处理。

本政策变更会在仓库公开，并更新日期。如对应用数据处理有疑问，可通过项目问题反馈渠道联系维护者，不必附带敏感信息。
