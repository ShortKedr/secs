using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: InspectAssemblyRefs <assembly.dll>");
    return 2;
}

try
{
    using var stream = File.OpenRead(args[0]);
    using var pe = new PEReader(stream);
    if (!pe.HasMetadata)
        throw new InvalidDataException("Assembly has no CLR metadata.");

    var metadata = pe.GetMetadataReader();
    if (!metadata.IsAssembly)
        throw new InvalidDataException("File is not a .NET assembly.");

    var references = metadata.AssemblyReferences
        .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
    var identity = new
    {
        assemblyVersion = metadata.GetAssemblyDefinition().Version.ToString(),
        references,
    };
    Console.WriteLine(JsonSerializer.Serialize(identity));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
