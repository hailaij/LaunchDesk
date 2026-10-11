# 更新下载与发行者签名 / Update Security

[中文 README](../README.md) | [English README](../README.en.md)

## 当前行为

启动后台读取固定仓库的最新正式发布版本，仅新版本提示，不自动下载。用户点击下载后才访问安装包。SHA-256、Windows Authenticode 信任链（含吊销检查）和编译时内置的签名叶证书 DER SHA-256 必须全部通过。网络、长度、哈希、签名、发行者或取消失败时删除临时文件，不修改当前安装与配置；清理失败会报告具体目录。

目前没有发行证书，默认 `UpdateSignerSha256` 为空，下载禁用。验证失败不能通过取消验签、自签名或信任任意发行者来绕过。安装仍由用户手动执行，不承诺安装失败自动还原。

## 接入发行签名

v0.6.0 已增加 [三阶段签名与发布流水线](trust-and-engineering.md)。本版本是未签名预发布，仅手动安装，不属于可信更新。可信更新发布必须使用仓库审核过的 `build/signing.json` 和公开 DER 证书；单独传入下面的参数构建预览，不构成可信发布批准。真实证书、SignPath 配置和受保护环境尚未接入，正式签名闭环仍待验收。

1. 获得发行签名服务及公开证书，如通过 SignPath 审批并配置受控构建。
2. 确认公开证书确实是 LaunchDesk 发行证书，计算 **证书原始 DER 数据的 SHA-256**，不是 Windows 常见 SHA-1 Thumbprint、PFX 文件哈希或安装包哈希。
3. 将该 64 位十六进制值通过 `-UpdateSignerSha256` 传入打包脚本。先构建可信证书固定的应用，再通过批准的签名服务签署最终安装包。
4. 验证已签安装包的 Windows 信任结果与发行者指纹。发布最终签名后的文件，确保 GitHub 返回该资产的 `sha256:` digest；签名前哈希不可复用。
5. 用具有同一信任固定值的旧版实际验证下载、签名和拒绝路径。首个受信任版本需人工安装；现在的无证书版本不能自动建立新信任。

```powershell
./scripts/build-installer.ps1 -CompilerPath 'D:\tool\Inno Setup 6\ISCC.exe' -UpdateSignerSha256 '<64-hex-DER-SHA256>'
```

此参数只配置验证信任，不会签名。不要将 PFX、私钥、密码或签名服务令牌提交仓库。证书轮换需要先发布信任迁移版本，目前仅支持单个叶证书固定；更换证书后旧版会拒绝新证书。Windows 吊销检查可能联网，网络无法完成信任校验时不降级接受。

## English

Startup checks query the fixed repository's latest stable release and only prompt for a newer version. Downloads require a user click. Asset SHA-256, Windows Authenticode trust (including revocation) and the compile-time **leaf certificate DER SHA-256** pin must all pass. Network, length, hash, signature, publisher or cancellation failures remove temporary packages, preserving the installed program and configuration. Cleanup failures report the remaining directory. There is no automatic installation or installation rollback.

No publisher certificate is currently configured; in-app downloads are disabled. Obtain the actual LaunchDesk publisher's public certificate from the approved signing service, verify its identity, and provide its 64-hex DER SHA-256 through `-UpdateSignerSha256`. This parameter configures trust only; it does not sign files. Sign the final installer through the approved service and publish its final hash. Never commit private keys or tokens. The first trusted version must be installed manually. Only one leaf certificate pin is supported; certificate rotation requires a trust-transition release or manual installation. Offline revocation failures are not downgraded to success.
