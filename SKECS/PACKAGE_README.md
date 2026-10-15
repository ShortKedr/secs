# ShortKedr.SKECS

SKECS is a small chunk-based entity-component-system library for C# applications
and games. Components are unmanaged structs; queries update their data through
typed `ref` parameters. Systems and queries run sequentially.

## Build a local package

SKECS 0.2.0 Branch is the breaking rename from the published SECS 0.1.0 Cone
release. From the repository root, build a local package:

```sh
dotnet pack SKECS/SKECS.csproj -c Release -o artifacts/release
```

Add the resulting directory as a NuGet source, then add the package to your
application:

```sh
dotnet add package ShortKedr.SKECS --version 0.2.0 --source path/to/folder
```

## Migrate from SECS 0.1.0

The published [`secs-v0.1.0` Cone release](https://github.com/ShortKedr/skecs/releases/tag/secs-v0.1.0)
remains available with the `ShortKedr.SECS` package, `SECS` namespace, and
original release assets. Upgrade a consumer by replacing the package reference
with `ShortKedr.SKECS`, changing `using SECS;` to `using SKECS;`, and updating
source or Unity DLL paths from `SECS` to `SKECS`. The old package and namespace
are not compatibility aliases for this breaking rename.

## Minimal example

```csharp
using SKECS;

using var world = new World();
var entity = world.CreateEntity(new Position { X = 2 });
ref var position = ref world.Get<Position>(entity);
position.X += 1;

struct Position { public int X; }
```

The public API is still evolving before `1.0.0`. Do not change component
membership while iterating a query or keep component references across
structural changes. A `World` is not thread-safe and must be disposed to free
its native storage.

See the [API and lifetime guide](https://github.com/ShortKedr/skecs/blob/main/docs/skecs/README.md)
and [0.2.0 Branch release notes](https://github.com/ShortKedr/skecs/blob/main/docs/skecs/RELEASE_NOTES_0.2.0.md).

License: MIT. Copyright (c) 2026 Nikolay Nakorkeshko.
