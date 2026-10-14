# ShortKedr.SECS

SECS is a small chunk-based entity-component-system library for C# applications
and games. Components are unmanaged structs; queries update their data through
typed `ref` parameters. Systems and queries run sequentially.

## Install from GitHub Release

Download `ShortKedr.SECS.0.1.0.nupkg` from the
[SECS v0.1.0 release](https://github.com/ShortKedr/secs/releases/tag/secs-v0.1.0)
into a local folder. Add that folder as a NuGet source, then add a package
reference to your application:

```sh
dotnet add package ShortKedr.SECS --version 0.1.0 --source path/to/folder
```

The package is not assumed to be available on NuGet.org. For Unity, use the
separate Unity ZIP attached to the same release. Godot .NET C# projects can add
the package through their `.csproj` after configuring the local source.

## Minimal example

```csharp
using SECS;

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

See the [API and lifetime guide](https://github.com/ShortKedr/secs/blob/main/docs/secs/README.md)
and [release version policy](https://github.com/ShortKedr/secs/blob/main/docs/secs/VERSIONING.md).

License: MIT. Copyright (c) 2026 Nikolay Nakorkeshko.
