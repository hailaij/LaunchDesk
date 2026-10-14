# 详细使用与开发参考

[返回 README](../README.md) | [English](advanced-guide.en.md)

以下内容描述当前 v0.9.0，命令从仓库根目录执行。日常操作见[使用指南](user-guide.md)。

## 运行

面向 Windows 10/11 x64。自包含发布包自带 .NET 运行时，解压后运行 `LaunchDesk.exe`，请保留整个目录。

从 [GitHub Releases](https://github.com/hailaij/LaunchDesk/releases/tag/v0.9.0) 手动下载 `LaunchDesk-v0.9.0-win-x64-setup.exe` 安装程序，或 `LaunchDesk-v0.9.0-win-x64.zip` 免安装包。各文件附带 `.sha256` 校验文件。本版本为正式发布，未签名，仅手动安装，不提供可信应用内更新。哈希不替代发行者签名。

在设置的“外观”中选择浅色、深色或跟随系统，保存后立即生效。旧版本配置无需手动迁移，未指定主题时默认跟随系统。升级时请先从托盘退出旧版本，再解压到新目录运行；便携模式用户需将原目录的 `portable.flag` 和 `data\` 复制到新版本目录，原数据保留备份。

首次运行为空列表，不自动添加或执行本机服务。点击添加图标创建启动项。关闭窗口会收起到托盘，彻底退出使用托盘菜单的“退出”；退出会停止全部受管进程并排空日志。

应用默认以普通用户权限运行，不绕过 PowerShell 执行策略，也不自动提权。

单实例机制会唤醒已经运行的实例，不会热替换旧版程序。窗口标题显示实际版本；新版发现其他版本或其他目录的实例时会提示其版本和路径。仅关闭窗口会隐藏到托盘，升级前必须使用托盘菜单的“退出”（这会停止旧实例管理的启动项）。资源管理器中 EXE 的文件类型仍是 Windows 标准的“应用程序”。

### 配置示例

工具栏的文件夹图标用于新建应用内分组，编辑启动项的“所属文件夹”可移动分组；选中文件夹后添加启动项会默认加入该组。文件夹“运行”启动组内项及其显式依赖（依赖可能位于其他文件夹），“停止”只停止组内项。删除文件夹仅解散分组，保留启动项、运行进程和日志，不修改磁盘上的脚本文件。运行中的单项需停止后才能编辑或删除。

| 场景 | 类型 | 资源 / 命令 | 参数 | 工作目录 |
|---|---|---|---|---|
| BAT 启动器 | BAT / CMD | `C:\Services\Chat\launcher.bat` | 按需填写 | `C:\Services\Chat` |
| npm 服务 | 命令（cmd） | `npm` | `run start` | `C:\Services\Bot` |
| 可执行程序 | 可执行程序 | `C:\Tools\worker.exe` | `--port 3000` | `C:\Tools` |
| PowerShell | PowerShell 脚本 | `C:\Scripts\worker.ps1` | 按需填写 | `C:\Scripts` |
| Git Bash | Shell 脚本 | `C:\Scripts\worker.sh` | 按需填写 | `C:\Scripts` |

BAT 未指定工作目录时，默认使用 BAT 所在目录。命令类型按 cmd 语法执行，npm 无需改成 EXE。Shell 类型必须明确指定解释器，例如 Git 的 `bin\bash.exe`；不要使用 WSL 的启动器代替 Git Bash。解释器应是可直接启动的 EXE。

环境变量每行 `NAME=VALUE`；参数按所选解释器的语法填写。新项默认自动编码：优先 UTF-8，无效字节时切换系统 ANSI；传统 Windows 程序可显式选择 GBK。已有编码不变；自动模式的标准输入为 UTF-8，显式编码同时用于显示和输入。JSONL 原始字节不受该选择影响。

### 数据与日志

默认数据目录：`%LOCALAPPDATA%\LaunchDesk`。

配置按序列化后的 UTF-8 字节计算，最多 4 MiB，超限会拒绝保存而不改动已有配置、导出文件或备份。每次保存使用独立临时文件；同一 Windows 会话内的并发提交串行执行，以最后提交的完整快照为准，不自动合并不同编辑。

- `config.json`：启动项和偏好。
- `backups\`：每次覆盖保存前的配置副本，保留 20 份。
- `backups\manual\`：手动配置快照，不受自动备份数量限制，不包含日志或脚本文件，可通过配置导入恢复。
- `logs\<启动项ID>\`：UTF-8 JSONL 日志。
- `diagnostics\errors.log`：本地异常诊断，约 2 MiB 轮转，另保留最多 3 份旧文件，不自动上传。

设置可切换便携模式。便携模式通过 EXE 旁的 `portable.flag` 启用，数据保存至 EXE 旁的 `data\`。支持在设置中选择或输入自定义数据目录；切换前须停止所有项，配置、日志、备份复制成功后立即使用新目录，原数据不删除，目标目录需为空且有写权限，不支持符号链接或目录联接。迁移失败的 `.migration-<ID>` 目录保留供排查。用户模式的目录记录位于 `%LOCALAPPDATA%\LaunchDesk-location.json`，便携标记优先于此记录。也可以在首次运行前自行放置 `portable.flag`。

日志默认每项保留 7 天、最多约 200 MiB。日志轮转、应用启动及每小时后台维护时清理，包括未运行项；单个记录可造成少量临时超额。面板最多保留 3,000 个记录片段、约 500,000 个字符，更早的内容通过导出查看。暂停显示和清空面板均不停止捕获，也不删除落盘日志；删除历史是独立操作。

导出 TXT 适合阅读；导出 JSONL 保留通道、时间与原始字节。stdout 与 stderr 各自的字节顺序保持完整，两条独立管道的交错次序按实际读取到的顺序记录，不能代表源程序的全局时间顺序。

配置可能含环境变量或私人路径，请仅导入可信配置，并在公开分享前检查内容。仓库不保存本机启动配置和运行日志。

### 进程生命周期

根进程先挂起创建，纳入独立 Windows Job Object 后才恢复执行；子进程继承该 Job。管理器异常结束时，关闭最后一个 Job 句柄会终止关联进程。根进程退出但子进程仍存活时，该项仍显示运行中，PID 列保留根进程 PID。

停止时优先发送已配置的退出输入，否则通过短生命周期助手向目标控制台发送 Ctrl+C；默认等待 10 秒，再终止整个受管进程树。默认依赖顺序只等待进程启动；新增的可选就绪检测可等待 TCP 连接成功、HTTP 2xx 响应或本次运行的日志关键词后再启动依赖项。批量停止按依赖的逆序进行。自动重启默认关闭，启用后间隔为 2、4、8、16 秒，连续退出 5 次暂停；持续运行超过一分钟后重置连续失败计数。日志写入失败时停止该项并报告错误。

就绪检测默认关闭，旧配置保持原行为。检测超时为 1–300 秒；日志关键词是区分大小写的文本，可跨输出片段匹配，不匹配历史日志。TCP/HTTP 只能证明目标端点可访问，不能确认它属于当前启动的进程，也不是持续健康监控；请使用独立端口或专用健康接口。HTTP 不跟随重定向。

拖拽运行中的启动项被禁用。拖到文件夹加入分组，拖到启动项前调整顺序并采用目标分组，拖到列表空白处移出分组。文件夹可拖拽排序，不支持嵌套。拖入文件会打开编辑窗口，Shell 仍需指定解释器。启动前检查不执行命令；复杂 cmd 表达式仍交由 cmd 解释，检查通过不保证服务自身不会报错。

按项提权每次请求 UAC，不保存密码；独立 LocalService 托管只支持后台脚本/控制台程序。不包含 WSL/Docker 内部生命周期或远程管理。启动器交由外部服务、已有 GUI 单实例进程或其他宿主管理的任务，可能不属于 Job 范围。GUI 程序只有实际写入 stdout/stderr 的数据可以捕获；仅写日志文件、未刷新缓冲、断电和存储故障不能承诺无损。

## 开发与验证

### 安装程序

安装脚本位于 `installer/LaunchDesk.iss`，需要 [Inno Setup 6](https://jrsoftware.org/isdl.php) 编译器。在项目目录执行：

```powershell
./scripts/build-installer.ps1
# 非标准编译器路径可使用 -CompilerPath 'C:\Tools\Inno Setup 6\ISCC.exe'
# 本机编译命令：
./scripts/build-installer.ps1 -CompilerPath 'D:\tool\Inno Setup 6\ISCC.exe'
```

输出为 `artifacts/installers/LaunchDesk-v0.9.0-win-x64-setup.exe` 及 SHA-256 校验文件。本版验证相同脚本和发布文件的隔离首次安装、升级数据保留及卸载；测试仅覆盖独立安装身份，不覆盖正在使用的正式安装。Windows 10 实机及真实服务验收暂缓，详见本版 Release 的验证范围。

默认安装到 `%LOCALAPPDATA%\Programs\LaunchDesk`，仅当前用户，无需管理员权限；创建开始菜单快捷方式，可选桌面快捷方式。完成后默认不启动应用，避免自动执行已配置服务。更新或卸载前请先从托盘退出实例；旧版便携程序需手动退出。卸载保留配置、日志、备份和便携数据，仅移除属于此安装路径的登录自启命令。安装器支持简体中文与英文，应用支持中英文切换。独立服务的注册、运行文件和数据不会随桌面卸载移除，应先在服务管理中卸载服务。未配置代码签名证书，生成的安装器不会带发行者签名。

目录用途与本地预览位置见 [项目结构](project-structure.md)。生成产物统一放在 `artifacts/`，源码、测试、脚本及文档保持独立。

需要 .NET SDK **10.0.401** 和 Windows x64。Host 使用 Microsoft 的 `System.ServiceProcess.ServiceController` 10.0.0（含 EventLog 依赖），依赖由锁文件固定；发布阶段从官方 NuGet 源还原 .NET 自包含运行时包。

```powershell
dotnet restore LaunchDesk.slnx --locked-mode
dotnet build LaunchDesk.slnx -c Release
dotnet run --project tests/LaunchDesk.Tests -c Release -- --report artifacts/test-results.json
dotnet publish src/LaunchDesk.App -c Release -r win-x64 --self-contained true -o artifacts/LaunchDesk-win-x64
```

测试使用隔离的临时目录和可中断测试进程。覆盖双通道大量输出、UTF-8 分块、GBK、无换行尾部、原始字节、BAT 相对路径和 pause、npm、输入、环境变量、依赖、重复启停、自动重启上限、日志错误、管理器被强制结束以及后代进程清理。

界面测试生成 WPF 实际布局的 PNG，覆盖标准和最小窗口尺寸、编辑窗口、设置窗口。已有三个真实服务的验收由用户暂缓，未启动或停止它们；隔离测试不代表已完成这些具体服务的实测。

```powershell
tests/LaunchDesk.UiChecks/bin/Release/net10.0-windows/LaunchDesk.UiChecks.exe --ui-smoke artifacts/ui.png
tests/LaunchDesk.UiChecks/bin/Release/net10.0-windows/LaunchDesk.UiChecks.exe --ui-smoke-min artifacts/ui-minimum.png
```

独立测试宿主的 `--ui-smoke` 使用隔离数据目录，截图中的启动项不会执行。生产程序拒绝全部 `--ui-*` 测试参数。GitHub Actions 还会验证签名样本、双语截图像素、虚拟化、异常恢复与进程清理，并上传报告及来源元数据，详见 [测试证据与故障隔离](test-evidence-and-fault-isolation.md)。

当前源码主测试清单为 107 组，每组在独立受管宿主内执行，120 秒超时后清理整个进程树；无签名样本时 3 组明确跳过。CI 的 `--fail-on-skip` 拒绝任何跳过，历史报告见 [测试证据](test-summary.md)。开发电脑可运行 `./tests/Verify-Ui.ps1` 和 `./tests/Verify-Engineering.ps1`；CI 自签证书脚本仅允许临时 GitHub-hosted runner。新增中英文 XAML 资源键门禁、1000 项配置与 200 MiB 日志验证，命令及持续运行边界见 [测试隔离与规模验证](scale-and-test-gates.md)。已发布 v0.8.0 的 104 组验证及资产验收见 Release 和 [本版复查](release-v0.8.0.md)，v0.9.0 收录的后续修复见 [管理器、服务诊断与 CLI JSON 复现](v0.8-review-manager-service-cli.md)及[日志显示边界](log-projection-boundary.md)。重复打包须使用新的输出目录，非空发布目录会被拒绝。
