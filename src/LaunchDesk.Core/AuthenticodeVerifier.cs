using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LaunchDesk.Core;

public sealed class AuthenticodeVerifier(string? trustedCertificateSha256 = null) : IUpdateSignatureVerifier
{
    public static string EmbeddedCertificateSha256 => typeof(AuthenticodeVerifier).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(x => x.Key == "UpdateSignerSha256")?.Value ?? "";
    public static bool IsConfigured => ValidPin(EmbeddedCertificateSha256);
    private static bool ValidPin(string pin) => pin.Length == 64 && pin.All(Uri.IsHexDigit);
    public void EnsureConfigured()
    {
        var pin = trustedCertificateSha256 ?? EmbeddedCertificateSha256;
        if (!ValidPin(pin)) throw new InvalidDataException("发行者签名尚未配置，无法验证更新包；旧版本保持不变。");
    }
    public string Verify(string path)
    {
        EnsureConfigured();
        var pin = trustedCertificateSha256 ?? EmbeddedCertificateSha256;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var info = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = Path.GetFullPath(path), Handle = file.SafeFileHandle.DangerousGetHandle() };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        Marshal.StructureToPtr(info, filePointer, false);
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, RevocationChecks = 1,
            UnionChoice = 1, File = filePointer, StateAction = 1, ProviderFlags = 0x80 | 0x2000 };
        try
        {
            var status = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (status != 0) throw new InvalidDataException($"Windows 签名验证失败（0x{status:X8}），安装包已拒绝。");
            var provider = WTHelperProvDataFromStateData(data.StateData);
            var signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            var certificate = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
            if (certificate == IntPtr.Zero) throw new InvalidDataException("无法读取已验证的发行者签名。");
            var header = Marshal.PtrToStructure<ProviderCertificateHeader>(certificate);
            if (header.Certificate == IntPtr.Zero) throw new InvalidDataException("发行者证书为空。");
            var context = Marshal.PtrToStructure<CertificateContext>(header.Certificate);
            if (context.Encoded == IntPtr.Zero || context.Length is 0 or > 1024 * 1024) throw new InvalidDataException("发行者证书无效。");
            var encoded = new byte[context.Length]; Marshal.Copy(context.Encoded, encoded, 0, encoded.Length);
            var actual = SHA256.HashData(encoded);
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(pin)))
                throw new InvalidDataException("安装包签名有效，但不属于受信任的 LaunchDesk 发行者。");
            using var publisher = X509CertificateLoader.LoadCertificate(encoded);
            return publisher.GetNameInfo(X509NameType.SimpleName, false);
        }
        finally
        {
            data.StateAction = 2;
            _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<TrustFile>(filePointer); Marshal.FreeHGlobal(filePointer);
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle, KnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size; public IntPtr PolicyCallbackData, SipClientData; public uint UiChoice, RevocationChecks, UnionChoice;
        public IntPtr File; public uint StateAction; public IntPtr StateData, UrlReference; public uint ProviderFlags, UiContext; public IntPtr SignatureSettings;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderCertificateHeader { public uint Size; public IntPtr Certificate; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CertificateContext { public uint Encoding; public IntPtr Encoded; public uint Length; public IntPtr Info, Store; }
    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint index, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint index);
}
