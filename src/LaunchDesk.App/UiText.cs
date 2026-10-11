using System.Windows;

namespace LaunchDesk;

internal static class UiText
{
    internal sealed class LabelSource : System.ComponentModel.INotifyPropertyChanged
    {
        public string this[string key] => T(key);
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public void Refresh() => PropertyChanged?.Invoke(this, new("Item[]"));
    }
    public static LabelSource Labels { get; } = new();
    public static string Language { get; private set; } = "zh-CN";
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal);
    static UiText()
    {
        foreach (var line in Catalog.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = line.IndexOf('|');
            English.Add(line[..split].Replace("\\n", "\n"), line[(split + 1)..].TrimEnd('\r').Replace("\\n", "\n").Replace("\\u0020", " "));
        }
        English["启动资源|*.bat;*.cmd;*.exe;*.ps1;*.sh|所有文件|*.*"] = "Launch resources|*.bat;*.cmd;*.exe;*.ps1;*.sh|All files|*.*";
        English["解释器|*.exe;*.cmd|所有文件|*.*"] = "Interpreters|*.exe;*.cmd|All files|*.*";
        English["文本日志 (*.txt)|*.txt|原始记录 (*.jsonl)|*.jsonl"] = "Text logs (*.txt)|*.txt|Raw records (*.jsonl)|*.jsonl";
        English["JSON 配置 (*.json)|*.json"] = "JSON configuration (*.json)|*.json";
        English["JSON 备份 (*.json)|*.json"] = "JSON backup (*.json)|*.json";
    }
    public static string T(string text) => Language == "en-US" && English.TryGetValue(text, out var value) ? value : text;
    public static string F(string text, params object[] args) => string.Format(T(text), args);
    public static string ErrorText(string message)
    {
        if (Language != "en-US") return message;
        if (English.TryGetValue(message, out var value)) return value;
        foreach (var entry in English.Where(x => x.Key.EndsWith('：')).OrderByDescending(x => x.Key.Length))
            if (message.StartsWith(entry.Key, StringComparison.Ordinal)) return entry.Value + message[entry.Key.Length..];
        var trust = System.Text.RegularExpressions.Regex.Match(message, "^Windows 签名验证失败（(0x[0-9A-F]{8})），安装包已拒绝。$");
        if (trust.Success) return "Windows signature verification failed (" + trust.Groups[1].Value + "). The installer was rejected.";
        var npm = System.Text.RegularExpressions.Regex.Match(message, "^package.json 未定义 scripts\\.(.+)；请检查工作目录或启动命令。$");
        if (npm.Success) return "package.json does not define scripts." + npm.Groups[1].Value + ". Check the working directory or command.";
        return message;
    }
    public static void Apply(string language)
    {
        Language = language == "en-US" ? language : "zh-CN";
        foreach (var key in English.Keys) Application.Current.Resources[key] = T(key);
        Labels.Refresh();
    }
    private const string Catalog = """
语言|Language
复制|Copy
全选|Select all
导出日志|Export logs
导出完成，跳过 {0} 条损坏记录；原始日志未修改。|Export completed; skipped {0} corrupt records. Original logs were not modified.
package.json 格式无效：|Invalid package.json:\u0020
package.json 须为 JSON 对象：|package.json must be a JSON object:\u0020
Ctrl+C 信号发送失败：|Ctrl+C signal failed:\u0020
管理器已强制终止进程树（0xE04C4401）。|The manager forcibly terminated the process tree (0xE04C4401).
输入队列等待超时。|Timed out waiting for the input queue.
输入发送超时，已停止受管进程树以释放阻塞管道。|Input timed out. The managed process tree was stopped to release the blocked pipe.
已从备份恢复配置：{0}\n损坏原件已保留：{1}\n本次启动不会自动启动任何启动项，请检查恢复内容。|Configuration restored from: {0}\nCorrupt original retained at: {1}\nAuto-start is disabled for this session. Please review the restored configuration.
备注|Note
退出输入延迟（秒）|Stop input delay (s)
退出输入前先发送 Ctrl+C|Send Ctrl+C before stop input
退出信号|Stop signal
0–300 秒；输入发送后才开始计算停止超时，进程提前退出则不再发送|0-300 seconds; stop timeout starts after input. No input is sent if the process exits first.
导入配置|Import configuration
导出配置|Export configuration
恢复配置备份|Restore backup
设置|Settings
新建文件夹|New folder
全部启动|Start all
全部停止|Stop all
添加启动项|Add launch item
编辑|Edit
复制启动项|Duplicate item
删除|Delete
上移|Move up
下移|Move down
搜索启动项|Search items
展开 / 收起文件夹|Expand / collapse folder
名称|Name
类型|Type
状态|Status
运行时长|Uptime
顺序|Order
退出码|Exit code
资源 / 命令|Target
自启|Auto
操作|Actions
运行|Run
停止|Stop
暂无启动项|No launch items
没有匹配的启动项|No matching items
启动所选项|Start selection
停止所选项|Stop selection
重启所选项|Restart selection
导出全部保留日志|Export retained logs
清空面板|Clear panel
删除历史日志|Delete log history
日志|Logs
暂停显示|Pause
自动滚动|Auto-scroll
全部通道|All channels
搜索日志|Search logs
发送输入和回车|Send input and Enter
标准输入|Standard input
就绪|Ready
文件夹|Folder
命令|Command
程序|Executable
是|Yes
未启动|Not started
等待就绪|Awaiting readiness
启动中|Starting
运行中|Running
停止中|Stopping
正常退出|Exited
检查失败|Check failed
异常退出|Failed
等待重启|Restart pending
便携模式|Portable mode
用户模式|User mode
合并日志|Combined logs
 · 合并日志| / Combined logs
 · 日志| / Logs
{0}/{1} 运行|{0}/{1} running
{0} 项 · {1} 运行中|{0} items / {1} running
永久删除所选范围内 {0} 个启动项的历史日志？|Permanently delete log history for {0} selected items?
替换当前配置为 {0} 个启动项？原配置将自动备份。导入项不会立即启动。|Replace the current configuration with {0} items? The original configuration will be backed up. Imported items will not start immediately.
请先停止文件夹的启动操作。|Stop the folder startup operation first.
其他启动项依赖该项，请先移除依赖。|Other items depend on this item. Remove those dependencies first.
导入前请停止全部启动项。|Stop all items before importing.
解散文件夹“|Dissolve folder "
”？启动项、运行进程和日志均保留。|"? Items, running processes and logs will be retained.
删除“|Delete "
”？日志将保留。|"? Logs will be retained.
正在下载 {0:F1} / {1:F1} MiB|Downloading {0:F1} / {1:F1} MiB
下载和签名验证通过，发行者：|Download and signature verified. Publisher:\u0020
。当前程序保持运行，安装需手动确认。|. The current program remains running. Installation requires manual confirmation.
下载已取消或超时，临时文件已清理；旧版本保持不变。|Download cancelled or timed out. Temporary files were removed. The old version is unchanged.
更新未完成：|Update incomplete:\u0020
\n旧版本和配置未被替换。|\nThe old version and configuration were not replaced.
\nCtrl+点击打开|\nCtrl+click to open
打开 LaunchDesk|Open LaunchDesk
退出|Exit
操作未完成|Operation incomplete
请先停止该启动项。|Stop this item first.
删除文件夹|Delete folder
删除启动项|Delete item
删除日志|Delete logs
配置备份|Configuration backup
备份完成：\n|Backup completed:\n
更改数据目录前请停止全部启动项。|Stop all items before changing the data directory.
将配置、日志与备份复制到新目录，并立即切换。原目录保留，不覆盖目标已有数据。继续？|Copy configuration, logs and backups to the new directory and switch now? The original directory is retained. Existing destination data will not be overwritten.
切换数据目录|Change data directory
设置已保存，但登录自启注册失败：|Settings saved, but login startup registration failed:\u0020
正在停止进程并保存日志…|Stopping processes and saving logs...
退出清理失败|Exit cleanup failed
浏览|Browse
取消|Cancel
保存|Save
启动项配置|Launch item settings
基本信息|General
未分组|Ungrouped
所属文件夹|Folder
可执行程序|Executable
命令（cmd）|Command (cmd)
PowerShell 脚本|PowerShell script
Shell 脚本|Shell script
启动参数|Arguments
工作目录|Working directory
解释器路径|Interpreter path
日志与环境|Logs and environment
日志 / 输入编码|Log / input encoding
环境变量|Environment variables
启动与停止|Startup and shutdown
依赖启动项|Dependencies
启动顺序|Startup order
0–9999，数字小的先启动；相同数字按列表顺序，显式依赖优先|0-9999: lower values start first. Ties follow list order. Explicit dependencies take precedence.
秒延迟|second delay
秒停止超时|second stop timeout
启动 / 停止|Start / stop
退出输入（可选）|Stop input (optional)
随管理器启动|Start with manager
退出后自动重启|Restart after exit
显示目标窗口|Show target window
选项|Options
服务就绪检测|Readiness detection
关闭|Close
关闭检测|Off
TCP 端口|TCP port
HTTP 成功响应|HTTP success response
日志关键词|Log keyword
检测方式|Method
主机|Host
端口|Port
HTTP 地址|HTTP URL
就绪超时（秒）|Ready timeout (s)
环境变量应为每行 NAME=VALUE。|Use one NAME=VALUE environment variable per line.
启动前检查|Preflight check
检查通过。|Checks passed.
启动前检查失败|Preflight failed
配置检查|Validation
配置无效|Invalid configuration
文件夹名称须为 1–100 个字符。|Folder names must contain 1-100 characters.
名称无效|Invalid name
外观|Appearance
跟随系统|System
浅色|Light
深色|Dark
主题|Theme
启动与窗口|Startup and window
用户登录后启动 LaunchDesk|Start LaunchDesk at login
启动时收起到托盘|Start minimized to tray
最小化时收起到托盘|Minimize to tray
启动|Startup
窗口|Window
数据目录|Data directory
数据与备份|Data and backups
选择空的数据目录|Select an empty data directory
当前目录|Current directory
打开数据目录|Open data directory
打开备份目录|Open backup directory
无法打开目录|Cannot open directory
立即备份配置|Back up configuration
备份失败|Backup failed
管理|Manage
日志保留|Log retention
日志保留（天）|Retention (days)
每项上限（MiB）|Item limit (MiB)
关于与开源|About and open source
启动时检查更新|Check for updates at startup
更新|Updates
关于 LaunchDesk|About LaunchDesk
数据目录必须是绝对路径。|The data directory must be an absolute path.
设置无效|Invalid settings
检查更新|Check for updates
打开下载页|Open download page
版本 |Version\u0020
Windows 启动项管理器|Windows launch item manager
版本更新|Version updates
开源项目|Open source
作者 / 维护者：hailaij 与社区贡献者|Author / maintainer: hailaij and community contributors
源代码仓库|Source repository
版本发布|Releases
问题反馈|Report an issue
源代码许可：MIT License|Source license: MIT License
第三方组件|Third-party components
使用 Microsoft .NET 10、WPF 与 WinForms。源代码没有第三方 NuGet PackageReference 依赖。自包含发行包的运行时许可和声明位于 runtime-notices 目录。|Built with Microsoft .NET 10, WPF and WinForms. No third-party NuGet PackageReference dependencies. Runtime licenses and notices are in runtime-notices in self-contained packages.
MIT 许可证|MIT license
第三方声明|Third-party notices
无法打开浏览器：|Cannot open browser:\u0020
正在查询 GitHub 正式发布版本…|Checking GitHub stable releases...
检查超时，请稍后重试。|The check timed out. Please try again later.
GitHub 请求受限，请稍后重试或打开版本发布页面。|GitHub rate limit reached. Try later or open the releases page.
无法连接 GitHub，请检查网络后重试。|Cannot connect to GitHub. Check your connection and retry.
发布信息无法解析，请打开版本发布页面查看。|Cannot read release information. Open the releases page.
暂无可用的正式发布版本。|No stable release is available.
当前已是最新正式版本（|You have the latest stable release (
当前版本高于最新正式发布版本（|Your version is newer than the latest stable release (
）。|).
查看更新 / 下载|View update / download
发现新版本 |New version available:\u0020
无法读取许可文件：|Cannot read license file:\u0020
自动下载|Download update
取消下载|Cancel download
打开下载目录|Open download directory
LaunchDesk 更新|LaunchDesk update
当前版本 v|Current version: v
发行者证书尚未配置，应用内下载已禁用。可打开发布页面查看。|The publisher certificate is not configured. In-app downloads are disabled. You can view the release page.
发布包缺少安装文件或校验信息，应用内下载已禁用。|The installer or checksum is missing. In-app downloads are disabled.
可下载并验证更新安装包。|The update installer can be downloaded and verified.
正在取消并清理临时文件…|Cancelling and cleaning up temporary files...
查看发布页面|View release page
稍后|Later
正在下载更新…|Downloading update...
下载完成，正在验证 SHA-256 和发行者签名…|Download complete. Verifying SHA-256 and publisher signature...
无法打开|Cannot open
新启动项|New launch item
名称须为 1–100 个字符。|Names must contain 1-100 characters.
文件夹名称重复。|Duplicate folder name.
目标文件夹不存在。|The target folder does not exist.
排序目标不属于该文件夹。|The ordering target does not belong to this folder.
配置文件超过 4 MiB。|The configuration file exceeds 4 MiB.
配置为空。|The configuration is empty.
不支持的配置格式。|Unsupported configuration format.
日志保留范围无效。|Invalid log retention range.
主题设置无效。|Invalid theme setting.
语言设置无效。|Invalid language setting.
文件夹名称为空、重复、过长或 ID 无效。|Folder names are empty, duplicated or too long, or IDs are invalid.
启动项为空、过多或 ID 重复。|Items are empty, exceed the limit or have duplicate IDs.
就绪检测配置无效：|Invalid readiness configuration:\u0020
TCP 就绪检测需要主机和有效端口。|TCP readiness requires a host and valid port.
HTTP 就绪检测需要不含用户名密码的 http/https 地址。|HTTP readiness requires an http/https URL without credentials.
日志就绪关键词长度须为 1–4096 个字符。|Log readiness keywords must contain 1-4096 characters.
文件夹与启动项 ID 重复。|Folder and item IDs overlap.
启动项所属文件夹不存在：|The item's folder does not exist:\u0020
启动项配置无效：|Invalid item configuration:\u0020
配置包含空值或 NUL。|The configuration contains null values or NUL.
环境变量格式无效。|Invalid environment variable format.
依赖不存在或引用自身：|A dependency is missing or references itself:\u0020
启动项依赖存在循环。|Launch dependencies contain a cycle.
数据目录记录无效，原数据未被修改。|Invalid data location record. The original data was not modified.
新旧数据目录不能互相包含。|Old and new data directories cannot contain each other.
数据目录不能经过符号链接或目录联接。|The data directory cannot traverse symbolic links or junctions.
请选择空目录，以免覆盖已有数据。|Choose an empty directory to avoid overwriting data.
迁移不支持符号链接：|Migration does not support symbolic links:\u0020
迁移不支持链接文件：|Migration does not support linked files:\u0020
启动命令为空。|The launch command is empty.
npm --prefix 缺少目录。|npm --prefix requires a directory.
npm 工作目录没有 package.json：|No package.json in the npm working directory:\u0020
package.json 超过 4 MiB。|package.json exceeds 4 MiB.
发行者签名尚未配置，无法验证更新包；旧版本保持不变。|The publisher signature is not configured. The update cannot be verified; the old version is unchanged.
发布包缺少安装文件、大小或 SHA-256 信息，请查看发布页面。|Installer, size or SHA-256 metadata is missing. View the release page.
更新包信息无效。|Invalid update package metadata.
更新缓存不能经过符号链接或目录联接。|The update cache cannot traverse symbolic links or junctions.
安装包长度与发布信息不符。|The installer length differs from the release metadata.
安装包超过声明大小。|The installer exceeds its declared size.
安装包下载不完整。|The installer download is incomplete.
SHA-256 校验失败，更新包已拒绝。|SHA-256 verification failed. The update was rejected.
下载文件在验证前发生变化。|The downloaded file changed before verification.
更新失败，临时文件清理失败：|Update failed and temporary file cleanup failed:\u0020
安装包重定向次数过多。|Too many installer redirects.
下载重定向没有地址。|The download redirect has no location.
下载地址不属于允许的 GitHub HTTPS 域名。|The download address is not an allowed GitHub HTTPS domain.
无法读取已验证的发行者签名。|Cannot read the verified publisher signature.
发行者证书为空。|The publisher certificate is empty.
发行者证书无效。|Invalid publisher certificate.
安装包签名有效，但不属于受信任的 LaunchDesk 发行者。|The signature is valid but does not belong to the trusted LaunchDesk publisher.
等待服务就绪|Waiting for readiness
服务已就绪|Service ready
程序正在退出。|The application is exiting.
依赖未保持运行：|Dependency is no longer running:\u0020
启动项未运行。|The item is not running.
文件夹不存在。|The folder does not exist.
找不到启动资源。|Launch resource not found.
找不到解释器或可执行程序：|Interpreter or executable not found:\u0020
工作目录不存在：|Working directory does not exist:\u0020
Shell 脚本需要指定解释器路径。|Shell scripts require an interpreter path.
新文件夹|New folder
 副本| copy
无法打开链接|Cannot open link
""";
}
