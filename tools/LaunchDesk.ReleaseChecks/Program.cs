using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using LaunchDesk.Core;

try
{
    if (args.Length != 3) throw new ArgumentException("Usage: --embedded-pin <Core.dll> <pin-or-empty> | --verify-file <file> <pin>");
    var path = Path.GetFullPath(args[1]); var pin = args[2];
    if (args[0] == "--embedded-pin")
    {
        var context = new AssemblyLoadContext("release-metadata", true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(path);
            var pins = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Where(x => x.Key == "UpdateSignerSha256").ToArray();
            if (pins.Length != 1 || !string.Equals(pins[0].Value, pin, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded publisher pin differs from reviewed build input.");
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", check = "embedded-pin", path }));
        }
        finally { context.Unload(); }
    }
    else if (args[0] == "--verify-file")
    {
        var publisher = new AuthenticodeVerifier(pin).Verify(path);
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", check = "signature-and-publisher", path, publisher }));
    }
    else throw new ArgumentException("Unknown release verification mode");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
