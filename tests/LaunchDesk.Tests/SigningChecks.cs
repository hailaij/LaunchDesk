using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using LaunchDesk.Core;

internal static class SigningChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        (string Path, string Pin) Fixture()
        {
            var manifest = Environment.GetEnvironmentVariable("LAUNCHDESK_SIGNED_FIXTURE");
            if (string.IsNullOrWhiteSpace(manifest) || !File.Exists(manifest))
                throw new TestSkippedException("No isolated signing fixture. Run tests/Invoke-CiVerification.ps1 on a disposable CI machine.");
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            var path = json.RootElement.GetProperty("signedPath").GetString()!;
            var cert = json.RootElement.GetProperty("certificatePath").GetString()!;
            var pin = json.RootElement.GetProperty("certificateSha256").GetString()!;
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(cert);
            if (certificate.GetCertHashString(HashAlgorithmName.SHA256) != pin || !File.Exists(path))
                throw new InvalidDataException("Signing fixture manifest does not match its public certificate/file.");
            return (path, pin);
        }
        void Reject(Action verify, bool publisher = false)
        {
            try { verify(); }
            catch (InvalidDataException error)
            {
                if (publisher && !error.Message.Contains("发行者")) throw new Exception("Wrong pin did not reach publisher comparison", error);
                return;
            }
            throw new Exception("Untrusted signature accepted");
        }
        await test("Signing: valid isolated Authenticode signature", () =>
        {
            var fixture = Fixture();
            if (string.IsNullOrWhiteSpace(new AuthenticodeVerifier(fixture.Pin).Verify(fixture.Path))) throw new Exception("Missing signer name");
            return Task.CompletedTask;
        });
        await test("Signing: wrong publisher rejected after native trust", () =>
        {
            var fixture = Fixture();
            Reject(() => new AuthenticodeVerifier(new string('0', 64)).Verify(fixture.Path), true);
            return Task.CompletedTask;
        });
        await test("Signing: modified signed PE rejected", () =>
        {
            var fixture = Fixture(); var path = Path.Combine(root, "tampered-fixture.exe"); File.Copy(fixture.Path, path);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            { file.Position = 512; var value = file.ReadByte(); if (value < 0) throw new InvalidDataException("Fixture too small"); file.Position = 512; file.WriteByte((byte)(value ^ 255)); }
            Reject(() => new AuthenticodeVerifier(fixture.Pin).Verify(path));
            return Task.CompletedTask;
        });
        await test("Signing: unsigned PE rejected", () =>
        {
            // The unsigned test apphost is a real PE, not a text file with an .exe suffix.
            var path = Path.Combine(root, "unsigned-fixture.exe");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "LaunchDesk.Tests.exe"), path);
            Reject(() => new AuthenticodeVerifier(new string('0', 64)).Verify(path));
            return Task.CompletedTask;
        });
    }
}
