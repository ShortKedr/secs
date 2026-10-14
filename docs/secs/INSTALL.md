English | [Русский](INSTALL.ru.md)

# Install SECS 0.1.0

SECS is an early ECS library for C# applications. Download the release assets
from the [`secs-v0.1.0` GitHub Release](https://github.com/ShortKedr/secs/releases/tag/secs-v0.1.0)
once it is published. The package is distributed as a `.nupkg` asset; it is not
published to NuGet.org by this release. Keep the versioned package or Unity ZIP
with your project so the dependency can be restored later.

## .NET applications

Use the release's `ShortKedr.SECS.0.1.0.nupkg` as a local NuGet source. For an
SDK-style project, place the package in a directory such as `packages/` and put
this `NuGet.Config` beside the project:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/>
  <add key="release" value="./packages" />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>
```

Then add the package:

```powershell
dotnet add package ShortKedr.SECS --version 0.1.0
```

The `netstandard2.0` asset used by .NET Framework 4.8 depends on
`System.Memory`; NuGet.org supplies that dependency unless it is also available
in an offline package source.

The planned targets for this release are .NET 8 and .NET Framework 4.8 on
Windows. A successful library build is not evidence that an application runs:
the release gate must run the package-backed consumer for both targets. Later
.NET SDKs can consume the compatible assets, but only tested runtimes belong in
the verified release matrix.

## Unity

Download the release's Unity ZIP. In a Unity 2022.3 LTS or Unity 6 project,
choose the **.NET Standard 2.1** API Compatibility Level. Copy all DLLs from
`lib/netstandard2.1/` in the ZIP into `Assets/Plugins/` and allow Unity to
reimport. The ZIP also includes the MIT license and this guide. Use the `SECS`
namespace in a C# script. The published release must include a verification
report with the exact Editor patches and backends that passed. The basic
`World`, `Query<T>().ForEach`, and `Get<T>`
API requires no `unsafe` setting in the consuming project. Direct chunk-pointer
iteration requires an `unsafe` context and project/compiler support for unsafe
code. Dispose every `World` to release its native buffers.

The release gate must run an imported-DLL smoke test in Editor/Mono and in a
Windows player built with Mono and IL2CPP for each supported Unity branch.
Platform-specific exports such as WebGL and mobile are outside this release's
verified matrix.

## Godot C#

Use Godot **4.5 .NET** on desktop. Put the `.nupkg` in a local source and add
the package to your C# project:

```xml
<ItemGroup>
  <PackageReference Include="ShortKedr.SECS" Version="0.1.0" />
</ItemGroup>
```

Use the same `NuGet.Config` shown above so restore can find both the local SECS
package and Godot's SDK on NuGet.org.

```powershell
dotnet restore YourGodotProject.csproj --configfile NuGet.Config
```

Use `SECS` from a C# node or other managed class. The release gate must run
the scene from [the Godot consumer fixture](../../tools/release-consumers/Godot)
with the package, then confirm the scene exited successfully. Godot C# Web and
mobile exports are not covered by this release.

## Minimal API check

The same check is used by the consumer fixtures: create an entity, run a query,
read its changed component, then dispose the world.

```csharp
using SECS;

using (var world = new World())
{
    var entity = world.CreateEntity(new Position { X = 2 });
    var processor = new Increment();
    world.Query<Position>().ForEach(ref processor);
    if (world.Get<Position>(entity).X != 3)
        throw new System.Exception("SECS query failed");
}

struct Position { public int X; }
struct Increment : IQueryProcessor<Position>
{
    public void Execute(Entity entity, ref Position position) => position.X++;
}
```

SECS components must be unmanaged structs. A `World` is not thread-safe;
structural changes and world disposal invalidate component references and chunk
pointers. See the [API and lifetime guide](README.md) before using those APIs.

## Verification status

On 2026-10-15, the local candidate `.nupkg` passed the independent
`CreateEntity → Query → Get → Dispose` consumer smoke on Windows x64:

| Consumer | Local result |
| --- | --- |
| .NET 8 (`net8.0`, SDK 8.0.426) | Passed; package restored from a local feed and run. |
| .NET 10 (`net10.0`, SDK 10.0.204) | Passed; app targeted and ran on .NET 10, selecting the compatible package asset. |
| .NET Framework 4.8 (`net48`, SDK 8.0.426) | Passed; Windows executable ran with the package and cached transitive dependencies supplied from an offline feed. |
| Godot 4.5 stable .NET, Windows x64 | Passed; official editor `4.5.stable.mono.official.876b29033` imported and ran the package-backed C# scene. |
| Unity 2022.3.60f1 | Editor and Windows Mono Player passed from isolated copies of the fixture; Windows IL2CPP was not run because its module is absent. |
| Unity 6000.3.19f1 | Editor, Windows Mono Player, and Windows IL2CPP Player passed from isolated copies of the fixture. |

The scripts in [release-consumers](../../tools/release-consumers) verify that
the restored SECS package matches the local candidate by SHA-256. These local
runs do not prove that a future clean release commit produces the same bytes
or that published assets have been downloaded and checked. Release notes must
list the exact versions and successful logs before claiming final support.
Until all required checks pass, this is installation documentation for a
planned release.
