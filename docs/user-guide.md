# LaunchDesk

Windows 10/11 x64 启动项管理器。运行 LaunchDesk.exe；应用自带运行时，无需另外安装 .NET。

首次使用没有示例启动项。应用会读取当前用户的已有配置，因此升级后仍保留原启动项和日志。

每个启动项可设置资源、参数、工作目录、环境变量和编码。npm 命令必须选择包含 package.json 的正确工作目录。文件夹是应用内分组，不移动实际文件；删除文件夹保留启动项。

窗口关闭会收起到托盘。托盘菜单的“退出”会停止受管进程。升级或卸载前请先退出旧实例。

默认配置及日志目录为 %LOCALAPPDATA%\LaunchDesk。卸载不会删除这些用户数据。便携模式使用程序旁的 portable.flag 和 data 目录。

日志面板支持过滤、清空、导出及输入。清空显示不删除历史日志；删除历史需单独确认。

项目及问题反馈：https://github.com/hailaij/LaunchDesk

设置中的“关于 LaunchDesk”提供版本、许可证和“检查更新”。仅在点击时查询 GitHub 正式发布版本；发现新版本后可查看更新说明并打开下载页，不自动下载、安装、退出应用或停止服务。

许可详见 LICENSE、THIRD-PARTY-NOTICES.md 和 runtime-notices 目录。
