using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using LaunchDesk.Core;

try
{
    if (args.Length != 3) throw new ArgumentException("Usage: --embedded-pin <Core.dll> <pin-or-empty> | --verify-file <file> <pin> | --production-boundary <App.dll> <empty>");
    var path = Path.GetFullPath(args[1]); var pin = args[2];
    if (args[0] == "--ui-smoke-inventory")
    {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var members = metadata.TypeDefinitions.SelectMany(handle =>
        {
            var type = metadata.GetTypeDefinition(handle);
            if (metadata.GetString(type.Name) != "MainWindow") return Array.Empty<string>();
            return type.GetMethods().Select(x => metadata.GetString(metadata.GetMethodDefinition(x).Name))
                .Concat(type.GetFields().Select(x => metadata.GetString(metadata.GetFieldDefinition(x).Name))).ToArray();
        }).Where(x => x is "smokePath" or "Capture" or "VerifyVirtualLogAsync" or "VerifyIncrementalLogAsync" or "RunSmokeAsync").ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { path, members, present = members.Length > 0 }));
    }
    else if (args[0] == "--production-boundary")
    {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var names = metadata.TypeDefinitions.Select(x => metadata.GetString(metadata.GetTypeDefinition(x).Name))
            .Concat(metadata.FieldDefinitions.Select(x => metadata.GetString(metadata.GetFieldDefinition(x).Name)))
            .Concat(metadata.PropertyDefinitions.Select(x => metadata.GetString(metadata.GetPropertyDefinition(x).Name)));
        if (names.Any(x => x.Contains("FaultProbe", StringComparison.Ordinal) || x == "UiSmokeRunner"))
            throw new InvalidDataException("Test-host member found in production assembly.");
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (metadata.GetString(type.Name) != "MainWindow") continue;
            var members = type.GetMethods().Select(x => metadata.GetString(metadata.GetMethodDefinition(x).Name))
                .Concat(type.GetFields().Select(x => metadata.GetString(metadata.GetFieldDefinition(x).Name)));
            if (members.Any(x => x is "smokePath" or "Capture" or "VerifyVirtualLogAsync" or "VerifyIncrementalLogAsync" or "RunSmokeAsync"))
                throw new InvalidDataException("UI smoke member found in production assembly.");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", check = "production-boundary", path }));
    }
    else if (args[0] == "--embedded-pin")
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
