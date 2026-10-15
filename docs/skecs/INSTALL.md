English | [Русский](INSTALL.ru.md)

# Install SKECS 0.2.0 Branch

SKECS 0.2.0 Branch introduces the breaking rename from SECS 0.1.0. This guide
shows how to build a local SKECS package from the repository root:

```powershell
dotnet pack SKECS/SKECS.csproj -c Release -o artifacts/release
```

The resulting `ShortKedr.SKECS.0.2.0.nupkg` is a local candidate. These
instructions use a local package source. Keep a copy with your project for
repeatable restore. See the [0.2.0 Branch release notes](RELEASE_NOTES_0.2.0.md)
for the migration steps.

## Migrate from SECS 0.1.0

The published [`secs-v0.1.0` Cone release](https://github.com/ShortKedr/skecs/releases/tag/secs-v0.1.0)
remains available with its `ShortKedr.SECS` package, `SECS` namespace, and
original release assets. SKECS 0.2.0 is a breaking rename. Update consumers:

- Replace the `ShortKedr.SECS` package reference with `ShortKedr.SKECS` version
  `0.2.0`.
- Change `using SECS;` to `using SKECS;`.
- If building from source, change `SECS/SECS.csproj` to `SKECS/SKECS.csproj`.
- If importing a Unity DLL directly, replace `SECS.dll` with `SKECS.dll`.

The former package, namespace, assembly and project paths are not compatibility
aliases. Keep them only when remaining on the historical 0.1.0 release.

## .NET applications

Use the locally built `ShortKedr.SKECS.0.2.0.nupkg` as a NuGet source. For an
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
dotnet add package ShortKedr.SKECS --version 0.2.0
```

The `netstandard2.0` asset used by .NET Framework 4.8 depends on
`System.Memory`; NuGet.org supplies that dependency unless it is also available
in an offline package source.

The historical SECS 0.1.0 consumer checks below do not validate SKECS 0.2.0.
Run consumer checks against the SKECS package before relying on a target runtime.

## Unity

The local [Unity ZIP builder](../../tools/release/make_unity_zip.py) packages
the same DLLs as the local `.nupkg`. In a Unity 2022.3 LTS or Unity 6 project,
choose the **.NET Standard 2.1** API Compatibility Level. Copy all DLLs from
`lib/netstandard2.1/` in the ZIP into `Assets/Plugins/` and allow Unity to
reimport. The ZIP also includes the MIT license and this guide. Use the `SKECS`
namespace in a C# script. The basic
`World`, `Query<T>().ForEach`, and `Get<T>`
API requires no `unsafe` setting in the consuming project. Direct chunk-pointer
iteration requires an `unsafe` context and project/compiler support for unsafe
code. Dispose every `World` to release its native buffers.

The historical SECS 0.1.0 checks ran an imported-DLL smoke test in Editor and
Windows players using Mono and IL2CPP. WebGL and mobile exports were not tested.

## Godot C#

Use Godot **4.5 .NET** on desktop. Put the `.nupkg` in a local source and add
the package to your C# project:

```xml
<ItemGroup>
  <PackageReference Include="ShortKedr.SKECS" Version="0.2.0" />
</ItemGroup>
```

Use the same `NuGet.Config` shown above so restore can find both the local SKECS
package and Godot's SDK on NuGet.org.

```powershell
dotnet restore YourGodotProject.csproj --configfile NuGet.Config
```

Use `SKECS` from a C# node or other managed class. The local
[Godot consumer fixture](../../tools/release-consumers/Godot) imported and ran
the package-backed scene. Godot C# Web and mobile exports were not tested.

## Minimal API check

The same check is used by the consumer fixtures: create an entity, run a query,
read its changed component, then dispose the world.

```csharp
using SKECS;

using (var world = new World())
{
    var entity = world.CreateEntity(new Position { X = 2 });
    var processor = new Increment();
    world.Query<Position>().ForEach(ref processor);
    if (world.Get<Position>(entity).X != 3)
        throw new System.Exception("SKECS query failed");
}

struct Position { public int X; }
struct Increment : IQueryProcessor<Position>
{
    public void Execute(Entity entity, ref Position position) => position.X++;
}
```

SKECS components must be unmanaged structs. A `World` is not thread-safe;
structural changes and world disposal invalidate component references and chunk
pointers. See the [API and lifetime guide](README.md) before using those APIs.

## Historical SECS verification record

The following results describe the SECS 0.1.0 (`ShortKedr.SECS`) candidate checked on 2026-10-15 before the rename. The published `secs-v0.1.0` Cone release retains that package, Unity ZIP, checksums, and verification JSON. These local Windows checks remain a separate pre-publication audit record and do not verify SKECS 0.2.0.

### Results

On 2026-10-15, the local candidate `.nupkg` passed the independent
`CreateEntity → Query → Get → Dispose` consumer smoke on Windows x64:

| Consumer | Local result |
| --- | --- |
| .NET 8 (`net8.0`, SDK 8.0.426) | Passed; package restored from a local feed and run. |
| .NET 10 (`net10.0`, SDK 10.0.204) | Passed; app targeted and ran on .NET 10, selecting the compatible package asset. |
| .NET Framework 4.8 (`net48`, SDK 8.0.426) | Passed; Windows executable ran with the package and cached transitive dependencies supplied from an offline feed. |
| Godot 4.5 stable .NET, Windows x64 | Passed; official editor `4.5.stable.mono.official.876b29033` imported and ran the package-backed C# scene. |
| Unity 2022.3.60f1 | Editor, Windows Mono Player, and Windows IL2CPP Player passed from isolated copies of the fixture. |
| Unity 6000.3.19f1 | Editor, Windows Mono Player, and Windows IL2CPP Player passed from isolated copies of the fixture. |

The scripts in [release-consumers](../../tools/release-consumers) verified that
the restored SECS package matched its local candidate by SHA-256. Those runs
used a local candidate from a working tree; the release's verification JSON and
`SHA256SUMS.txt` document the published assets.
