# Release consumer smoke tests

These projects consume built release artifacts, not `SKECS.csproj`. Each test
creates a `World`, creates an entity, runs a query, reads the changed component,
and disposes the world. A nonzero exit code or missing success marker fails the
smoke. `-Version` must match the package version being released.

```powershell
pwsh tools/release-consumers/Invoke-DotNetSmoke.ps1 -PackageDirectory artifacts/release -Framework net8.0 -Version 0.2.0
pwsh tools/release-consumers/Invoke-DotNetSmoke.ps1 -PackageDirectory artifacts/release -Framework net10.0 -Version 0.2.0
pwsh tools/release-consumers/Invoke-DotNetSmoke.ps1 -PackageDirectory artifacts/release -Framework net48 -Version 0.2.0
pwsh tools/release-consumers/Invoke-GodotSmoke.ps1 -PackageDirectory artifacts/release -GodotExecutable C:/path/to/Godot_v4.5-stable_mono_win64_console.exe -Version 0.2.0
pwsh tools/release-consumers/Invoke-UnitySmoke.ps1 -UnityZip C:/path/to/unity-release.zip -UnityEditor C:/path/to/Unity.exe -Backend Editor -Version 0.2.0
pwsh tools/release-consumers/Invoke-UnitySmoke.ps1 -UnityZip C:/path/to/unity-release.zip -UnityEditor C:/path/to/Unity.exe -Backend Mono -Version 0.2.0
pwsh tools/release-consumers/Invoke-UnitySmoke.ps1 -UnityZip C:/path/to/unity-release.zip -UnityEditor C:/path/to/Unity.exe -Backend IL2CPP -Version 0.2.0
```

The .NET and Godot runners use an isolated NuGet cache and compare SHA-256 of
the restored `ShortKedr.SKECS` package with the supplied `.nupkg`. They use
NuGet.org only for external dependencies. The `net48` runner requires Windows
and can use `-Offline` when its package directory also contains all dependency
`.nupkg` files. The Godot runner requires the 4.5 .NET editor executable and
runs import and the scene, checking `SKECS_GODOT_OK`.

The Unity runner imports DLLs from the supplied ZIP into a temporary copy of
the fixture project, checks the SKECS assembly version, runs the Editor check,
or builds and runs a Windows x64 Mono/IL2CPP player. Each Unity branch and
backend must pass separately. Unity generates files only in the ignored
`artifacts/release-consumers/` copy. A fixture source file alone does not prove
Unity runtime compatibility.
