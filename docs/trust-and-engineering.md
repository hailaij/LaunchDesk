# Trust And Engineering / 可信度与工程化

Included in v0.6.0 (unsigned prerelease, 2026-10-12). The original local verification below
was performed on 2026-10-11 before the version bump. Cloud CI acceptance is recorded separately.
No real services were interrupted; no production signing approval or silent installation is implied.

Current scope and evidence index: [Verification status](verification-status.md).
The local working tree has 76 test groups; remote commit `43449c5` has 73.

## 测试诚实性

核心报告保持 JSON 数组，增加 `status`（passed/failed/skipped）、`skipped`、`reason`。
`passed` 在跳过时为 null，不能把未执行算作通过。`--fail-on-skip` 使任何跳过返回退出码 1；
无匹配用例返回 2。`tests/Assert-TestReport.ps1` 验证当前 76 组、名称唯一、状态一致、零失败，
CI 额外要求零跳过。新增或删除用例必须同步这个清单计数。

日志增量刷新、界面性能预算与负向门禁测试见 [Log Performance](log-performance.md)。

4 个独立签名组覆盖有效签名、错误发行者、篡改后的 PE、真实未签名 PE。
没有指定样本时，前三组明确跳过，不再读取固定 D 盘路径或假装通过。
已指定但损坏的样本是失败，不是跳过。`Verify-Engineering.ps1` 实测这些退出码与报告语义，
也覆盖缺失生产配置、内嵌 pin 不匹配、未签名发行物和禁止本机导入测试根证书。

## 隔离签名样本

`tests/New-SigningFixture.ps1` 仅允许临时 GitHub-hosted Windows runner，拒绝开发电脑和
self-hosted runner。CI 在 CurrentUser/My 创建有效期两天的临时代码签名证书，导入同一
runner 的 LocalMachine/Root，用 Windows SDK signtool 签署测试 apphost，导出公开 CER 和
样本清单。私钥不导出到文件、不上传；CI 包装器 finally 按该证书指纹移除根证书和私钥。
生成失败也有清理路径，runner 销毁是最后一层隔离。

样本 pin 仅通过 `LAUNCHDESK_SIGNED_FIXTURE` 交给测试中显式构造的验证器，不写入生产
MSBuild 参数、signing.json 或发布物。不要伪造 runner 环境变量在日常电脑运行此脚本。
这不是绕过 Windows 信任链：生产 `AuthenticodeVerifier` 未添加接受自签名的分支。

2026-10-12 云端准备阶段发现 CurrentUser Root 导入停滞；改用 Windows
[`certutil -f -addstore`](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/certutil)
在受限制、具有管理员权限的临时 runner 的 LocalMachine/Root 中完成无交互导入；
CurrentUser/Root 即使通过 certutil 仍会等待交互确认。增加阶段日志与五分钟验收超时。
这不允许在开发电脑运行，也不改变生产发行者 pin 或生产验签策略。

## CI 与工程规范

- `windows.yml` 用锁文件还原、零警告构建、真实签名样本、核心测试、双语界面和异常清理。
- `Verify-Ui.ps1` 检查进程退出码、截图尺寸和采样像素非空、3 万行虚拟化断言、真实 timer
  恢复及加载异常。致命异常 fixture 与观察脚本通过文件握手，避免依赖固定睡眠观察子进程。
- 失败时仍上传 JSON/PNG/错误证据；开发包明确标作 unsigned preview，不是可信更新。
- 所有 Actions 固定 40 位 SHA，checkout 不保留 Git 凭据；Dependabot 跟踪 Actions/NuGet。
- SDK 唯一来源 global.json；版本唯一来源 Directory.Build.props；manifest 构建生成版本。
- 加入 NuGet 锁文件/source mapping、CI deterministic 属性、editorconfig、gitattributes、SECURITY.md。
- Inno 工具链版本、官方安装器摘要和已核验编译器摘要在 build/toolchain.json。
  云端通过标准包管理器安装该版本，编译前验证编译器摘要及 Windows 签名；不自动修改本机环境。

## 正式签名与发布

`release.yml` 仅手动从 main 发起，输入已存在且指向该次 main 提交的版本标签。
缺少签名令牌、生产证书或任何服务配置时，在构建/发布前失败；没有 unsigned fallback。
现有 Release 不覆盖，签名失败、超时、验签失败不会执行 publish job。

准备接入时需要维护者审核并提交：

1. `build/signing.json` 的真实叶证书 DER SHA-256、仓库内公开 CER 路径、SignPath 组织/
   项目/生产策略，以及 App、Uninstaller、Installer 三个独立 artifact configuration slug。
2. 服务侧 App 配置只签 LaunchDesk.exe、LaunchDesk.dll、LaunchDesk.Core.dll，不签第三方
   runtime。Uninstaller 配置识别 Inno 生成的 `uninst-*.e32` PE，Installer 配置只签版本化 setup.exe。
   按实际上传样本生成并审核约束，不使用宽泛的任意文件签名策略。
3. GitHub 的 production-signing / production-release 环境配置 main 限制及必要人工审核，
   并仅在签名环境保存 SIGNPATH_API_TOKEN；私钥不提交仓库。保护 main 和版本标签。

流程顺序：

1. 完整验收后，构建固定生产 pin 的应用，并从实际 DLL 读取内嵌值核对。
2. 上传三个自有 PE 到 GitHub artifact，SignPath 校验来源并签名；验 Windows 信任链、
   精确发行者 pin、时间戳及内嵌 pin 后，只导入允许的文件清单。
3. Inno `SignedUninstaller=yes` 首轮输出待签名 `.e32` 后按设计拒绝编译。
   只接受新空目录内恰好一个未签名缓存文件，不能把其他编译错误当成功。
4. SignPath 签署卸载器，验证后再次编译；Inno 把该已签缓存嵌入安装包。
5. SignPath 签署最终 setup.exe，再验签。最终签名后才生成安装器、便携 ZIP 的哈希与清单；
   签名前的开发 ZIP/哈希不会作为发布输入。
6. 独立发布 job 再次读取仓库证书、核对 Windows 签名、发行者、时间戳、哈希、大小及资产
   清单，验证便携包的自有 PE；先建立草稿，再公开。失败可留下草稿，但不自动覆盖已有版本。

Inno 使用官方支持的手工签名卸载器缓存流程，而非提供空的 SignTool 或伪签名回调。
参考：[Inno SignedUninstaller](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm)、
[SignPath GitHub origin verification](https://docs.signpath.io/trusted-build-systems/github)。

公开证书必须与审核过的 pin 一致；带私钥、自签名或 CI fixture 证书不能配置生产。
便携 ZIP 的 SHA-256 不是 ZIP 的发行者数字签名；自有 PE 有 Authenticode，应用内更新只
下载验签后的 setup.exe。仍不静默安装、不承诺安装失败自动还原，单叶 pin 的轮换尚需迁移版本。

## 原始本机验证（历史记录）

- 本机锁定还原及构建成功，零警告/错误；生成 manifest 为 0.5.0.0。
- `artifacts/verification/engineering/test-results.json`：73 passed、0 failed、0 skipped。
  **本机签名样本是已有受信任 Inno 编译器及其公开证书，不是 CI 自签证书**；测试不修改原文件。
- `contracts/results.json`：6 个工程门禁检查通过；其 skipped.json / failed.json 是预期的
  负例报告，不能与全套报告混为一谈。缺样本证明为 1 passed、3 skipped，严格模式退出 1。
- `ui-accepted/summary.json`：中英文、截图像素、timer 恢复、虚拟化与异常清理通过。
  两个致命异常 fixture 的根进程及各两个观察到的子进程均退出，诊断文件存在。
- 零 pin 的实际开发打包核对通过；另一个隔离 Core 构建的非空 pin 读取核对通过，错 pin 拒绝。
- Inno 6.7.3 的待签 `.e32` 准备阶段实际通过，未伪造已签安装器。
- PowerShell 脚本语法及 YAML 结构解析通过。此段为最初的本机验收记录；随后完成的
  GitHub-hosted 自签与 CI 演练见下一节。SignPath 来源策略、正式三阶段签名及可信公开发布仍未执行。
- 未导入本机测试根证书；未安装工具链；未更改安全设置。原来的互斥锁与更新缓存竞态加固
  不属于这次工程化完成声明。

## 云端隔离签名演练（2026-10-12）

[Actions run 38215650657](https://github.com/hailaij/LaunchDesk/actions/runs/38215650657)
针对远端 `main` 提交 `43449c5a876096dd0af75fcce9fa482e78d342f9`，完整 workflow 成功。
主测试报告为 **73 passed、0 failed、0 skipped**，4 种签名场景均实际执行；5 个工程负向门禁、
双语界面及致命异常清理通过。详细证据及限制见 [隔离签名演练](signing-drill.md)。

清理阶段日志和成功退出证明严格删除命令正常完成，但没有独立的清理后证书库快照。
本机未导入证书，测试私钥未导出。此次 CI **不包含**本地尚未推送的 Windows 10 兼容改动、
日志增量刷新与性能门禁，也不代表正式 SignPath 签名或安装包生产签名完成。

## English Summary

Test reports distinguish passed, failed and skipped; strict CI rejects any skip. CI-generated
Authenticode fixtures and temporary trust roots are restricted to disposable GitHub-hosted
Windows runners, with private-key/root cleanup and no production pin injection. UI pixel,
virtualization, recovery and fatal process-cleanup tests are CI gates. Actions are SHA-pinned,
restore is locked, and SDK/version sources are unified.

The protected manual release workflow signs application, Inno cached uninstaller and final setup
in separate SignPath requests. Native publisher/timestamp/embedded-pin verification precedes
final hashing; a separate job rechecks the signed handoff before draft/public release. Missing
production signing configuration blocks publication. No signing approval, private key or silent
installation is implied. Local tests pass with an existing publicly signed native fixture.
The disposable CI fixture and full verification workflow passed on remote commit `43449c5`
(73 groups, zero failures/skips), run 38215650657. Uncommitted local changes have 76 locally
passing groups but no cloud acceptance yet. The production SignPath/release path remains untested.
