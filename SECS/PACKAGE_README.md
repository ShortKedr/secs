# ShortKedr.SECS

SECS is a small chunk-based entity-component-system library for C# applications
and games. Components are unmanaged structs; queries update their data through
typed `ref` parameters. Systems and queries run sequentially.

## Use a local package

No `secs-v0.1.0` GitHub Release has been published. From the SECS repository
root, build `ShortKedr.SECS.0.1.0.nupkg` locally:

```sh
dotnet pack SECS/SECS.csproj -c Release -o artifacts/release
```

Add the resulting directory as a NuGet source, then add a package reference
to your application:

```sh
dotnet add package ShortKedr.SECS --version 0.1.0 --source path/to/folder
```

These instructions use a local package source. For Unity, build a local Unity ZIP
from the same package. Godot .NET C# projects can add the local package through
their `.csproj` after configuring the source.

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

See the [API and lifetime guide](https://github.com/ShortKedr/secs/blob/main/docs/secs/README.md).

License: MIT. Copyright (c) 2026 Nikolay Nakorkeshko.
