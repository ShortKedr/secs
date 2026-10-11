param(
    [string]$OutputDirectory = "Benchmarks.Local/tmp/web-coreclr-r2r",
    [string]$DotnetExecutable = "Benchmarks.Local/tmp/dotnet11-coreclr/dotnet.exe",
    [string]$NodeExecutable = "node",
    [string]$BasePath = "/",
    [switch]$Interpreter,
    [switch]$AllowPartial
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
function ProjectPath([string]$relative) {
    $full = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path must remain inside the project: $full"
    }
    return $full
}
if (-not $IsWindows) { throw "This pinned experimental helper currently requires Windows x64." }
if ($BasePath -notmatch '^/([a-zA-Z0-9_-]+/)*$') { throw "BasePath must be an absolute URL directory." }
$output = ProjectPath $OutputDirectory
if (Test-Path -LiteralPath $output) { throw "Output exists; choose a new OutputDirectory to avoid stale artifacts." }
$dotnet = ProjectPath $DotnetExecutable
if (-not (Test-Path -LiteralPath $dotnet)) { throw "Install the pinned .NET 11 SDK and wasm-tools inside the project first; see SpaceBattle.Web.CoreClr/README.md." }
$sdk = Split-Path -Parent $dotnet
$env:DOTNET_ROOT = $sdk
$env:DOTNET_ROOT_X64 = $sdk
$env:DOTNET_CLI_HOME = ProjectPath 'Benchmarks.Local/.dotnet'
$env:NUGET_PACKAGES = ProjectPath 'Benchmarks.Local/nuget-packages'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$work = ProjectPath ("Benchmarks.Local/tmp/coreclr-build-" + [guid]::NewGuid().ToString('N'))
$artifacts = Join-Path $work 'artifacts'
$project = Join-Path $root 'SpaceBattle.Web.CoreClr/SpaceBattle.Web.CoreClr.csproj'
$mode = if ($Interpreter) { 'false' } else { 'true' }
$common = @('-c', 'Release', '--disable-build-servers', '--artifacts-path', $artifacts,
    '-m:1', '-nr:false', '-p:UseSharedCompilation=false', "-p:SpaceBattleCoreClrR2R=$mode")
Push-Location (Join-Path $root 'SpaceBattle.Web.CoreClr')
try {
    $version = & $dotnet --version
    if ($LASTEXITCODE -ne 0 -or $version.Trim() -ne '11.0.100-rc.1.26425.128') { throw "Wrong SDK: $version" }
    $firstOutput = if ($Interpreter) { $output } else { Join-Path $work 'il' }
    & $dotnet publish $project @common -o $firstOutput "-bl:$work/il.binlog"
    if ($LASTEXITCODE -ne 0) { throw "CoreCLR IL publish failed ($LASTEXITCODE)." }
    if (-not $Interpreter) {
        $linked = Join-Path $artifacts 'obj/SpaceBattle.Web.CoreClr/release/linked'
        $r2r = Join-Path $work 'r2r'
        New-Item -ItemType Directory -Path $r2r -Force | Out-Null
        $compiler = Join-Path $env:NUGET_PACKAGES 'microsoft.netcore.app.crossgen2.win-x64/11.0.0-rc.1.26425.128/tools/crossgen2.exe'
        if (-not (Test-Path -LiteralPath $compiler)) { throw 'Pinned crossgen2 is missing.' }
        $rootsProject = Join-Path $root 'tools/CoreClrR2RRoots/CoreClrR2RRoots.csproj'
        & $dotnet build $rootsProject -c Release --disable-build-servers --artifacts-path "$work/roots-build" -m:1 -nr:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw 'Static R2R root generator build failed.' }
        $roots = Join-Path $r2r 'static.mibc'
        & $dotnet "$work/roots-build/bin/CoreClrR2RRoots/release/CoreClrR2RRoots.dll" $linked $roots
        if ($LASTEXITCODE -ne 0) { throw 'Static R2R root generation failed.' }
        $inputHashes = @{}
        foreach ($assembly in (Get-ChildItem -LiteralPath $linked -Filter *.dll)) {
            $inputHashes[$assembly.Name] = (Get-FileHash -LiteralPath $assembly.FullName -Algorithm SHA256).Hash
            $options = @()
            if ($assembly.BaseName -in @('SpaceBattle.Web', 'SpaceBattle.Core', 'SpaceBattle.SECS', 'SECS', 'Aether.Physics2D')) {
                $options = @('--compilebubblegenerics', '--non-local-generics-module:*', '--opt-cross-module:*', "-m:$roots")
            }
            $compilerArguments = @($assembly.FullName, "-r:$linked/*.dll", '--targetos:browser', '--targetarch:wasm', '--obj-format:wasm',
                "--out:$r2r/$($assembly.BaseName).wasm", '-O', '--verbose') + $options
            $compilerArguments | ConvertTo-Json | Out-File -LiteralPath "$r2r/$($assembly.BaseName).args.json" -Encoding utf8
            & $compiler @compilerArguments 2>&1 | Tee-Object -FilePath "$r2r/$($assembly.BaseName).log" | Select-Object -Last 1
            if ($LASTEXITCODE -ne 0) { throw "crossgen2 failed: $($assembly.Name). See $r2r" }
        }
        & $dotnet publish $project @common --no-restore -o $output "-p:LocalR2RDir=$r2r" "-bl:$work/r2r.binlog"
        if ($LASTEXITCODE -ne 0) { throw "R2R staging failed ($LASTEXITCODE)." }
        foreach ($assembly in (Get-ChildItem -LiteralPath $linked -Filter *.dll)) {
            if ((Get-FileHash -LiteralPath $assembly.FullName -Algorithm SHA256).Hash -ne $inputHashes[$assembly.Name]) {
                throw "IL changed between compilation and staging: $($assembly.Name)"
            }
        }
        Copy-Item -LiteralPath (Join-Path $r2r 'static.roots.json') -Destination "$output/wwwroot/r2r-static-roots.json"
        $auditOptions = if ($AllowPartial) { @('--allow-partial') } else { @() }
        & $NodeExecutable --experimental-wasm-exnref "$root/tools/web/audit-coreclr-r2r.mjs" "$output/wwwroot" $r2r @auditOptions
        if ($LASTEXITCODE -ne 0) { throw "R2R coverage/integrity gate failed. Partial output is diagnostic only: $output" }
        Copy-Item -LiteralPath (Join-Path $artifacts 'obj/SpaceBattle.Web.CoreClr/release/wasm/for-publish/dotnet.native.js.symbols') -Destination "$output/wwwroot/r2r-native-symbols.txt"
    }
    $site = Join-Path $output 'wwwroot'
    $licenses = Join-Path $site 'licenses'
    $runtimePack = Join-Path $sdk 'packs/Microsoft.NETCore.App.Runtime.browser-wasm/11.0.0-rc.1.26425.128'
    $emscripten = Join-Path $sdk 'packs/Microsoft.NET.Runtime.Emscripten.6.0.2.Sdk.win-x64/11.0.0-rc.1.26425.128/tools/emscripten'
    Copy-Item -LiteralPath "$runtimePack/LICENSE.TXT" -Destination "$licenses/DOTNET.txt"
    Copy-Item -LiteralPath "$runtimePack/THIRD-PARTY-NOTICES.TXT" -Destination "$licenses/DOTNET-THIRD-PARTY.txt"
    Copy-Item -LiteralPath "$env:NUGET_PACKAGES/microsoft.aspnetcore.components.webassembly/11.0.0-rc.1.26425.128/THIRD-PARTY-NOTICES.txt" -Destination "$licenses/ASPNETCORE-THIRD-PARTY.txt"
    Copy-Item -LiteralPath "$emscripten/LICENSE" -Destination "$licenses/EMSCRIPTEN.txt"
    Copy-Item -LiteralPath "$emscripten/AUTHORS" -Destination "$licenses/EMSCRIPTEN-AUTHORS.txt"
    $index = Join-Path $site 'index.html'
    $html = [IO.File]::ReadAllText($index)
    $html = [Text.RegularExpressions.Regex]::Replace($html, '<base href="[^"]*"\s*/?>', '<base href="' + $BasePath + '">')
    [IO.File]::WriteAllText($index, $html, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $site '.nojekyll'), '', [Text.UTF8Encoding]::new($false))
    if ($AllowPartial) { Write-Warning 'Partial R2R does not establish compiled hot loops; do not deploy it as a performance upgrade.' }
    if (-not $Interpreter) { Write-Warning 'CoreCLR R2R is experimental and may execute hot callees in the interpreter. Static coverage is not runtime verification or a speedup claim.' }
    Write-Output "CoreCLR site: $site; build evidence: $work"
}
finally { Pop-Location }
