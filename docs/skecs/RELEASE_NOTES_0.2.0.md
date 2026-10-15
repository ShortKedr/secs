# SKECS 0.2.0 — Branch

SKECS 0.2.0 introduces the breaking product and public API identity change
from SECS 0.1.0. This rename happens before 1.0.0.

## Migration

- Replace the `ShortKedr.SECS` package with `ShortKedr.SKECS` version `0.2.0`.
- Change C# imports from `using SECS;` to `using SKECS;`.
- Update source project paths from `SECS/SECS.csproj` to
  `SKECS/SKECS.csproj`, and the solution from `SECS.sln` to `SKECS.sln`.
- For Unity, remove `SECS.dll` and import `SKECS.dll` instead.

The old package and namespace are not compatibility aliases. Consumers that
stay on the old identity can continue using the published
[`secs-v0.1.0` Cone release](https://github.com/ShortKedr/skecs/releases/tag/secs-v0.1.0),
including its original package, Unity ZIP, checksums, and verification JSON.
Those release assets and their verification apply to SECS 0.1.0 only.
