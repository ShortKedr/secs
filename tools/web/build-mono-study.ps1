param(
    [string]$Study = "Benchmarks.Local/tmp/mono-study-20261012",
    [ValidateSet("Freeze", "A", "B", "C", "D", "Profile")][string]$Variant = "Freeze",
    [string]$Sdk = "Benchmarks.Local/tmp/dotnet-aot",
    [switch]$BaselineHeap,
    [switch]$PrivateLtoCache
)
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
function LocalPath([string]$relative) {
    $full = [IO.Path]::GetFullPath((Join-Path $root $relative))
    $allowed = Join-Path $root "Benchmarks.Local/tmp"
    if (-not $full.StartsWith($allowed + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Study and SDK must stay in ignored Benchmarks.Local/tmp: $full"
    }
    return $full
}
$studyPath = LocalPath $Study
$sdkPath = LocalPath $Sdk
$dotnet = Join-Path $sdkPath "dotnet.exe"
$env:DOTNET_ROOT = $sdkPath
$env:DOTNET_ROOT_X64 = $sdkPath
$env:PATH = "$sdkPath;$env:PATH"
$env:DOTNET_CLI_HOME = Join-Path $root "Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = Join-Path $root "Benchmarks.Local/nuget-packages"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = "false"
$env:TEMP = Join-Path $root "Benchmarks.Local/tmp"
$env:TMP = $env:TEMP
if ($Variant -eq "Freeze") {
    if (Test-Path -LiteralPath $studyPath) { throw "Use a fresh study directory" }
    $source = Join-Path $studyPath "source"
    New-Item -ItemType Directory -Path $source | Out-Null
    $manifest = @()
    foreach ($project in @("SpaceBattle.Web", "SpaceBattle.Core", "SpaceBattle.SKECS", "SKECS")) {
        $directory = Join-Path $root $project
        foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
            if ($relative -match '[/\\](bin|obj|node_modules)[/\\]') { continue }
            $target = Join-Path $source $relative
            New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target
            $manifest += @{ path = $relative; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
        }
    }
    foreach ($file in Get-ChildItem (Join-Path $root "SpaceBattle.Game/Assets") -File) {
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
        $target = Join-Path $source $relative
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
        $manifest += @{ path = $relative; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    }
    foreach ($name in @("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.Config", "nuget.config")) {
        $file = Join-Path $root $name
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            Copy-Item -LiteralPath $file -Destination (Join-Path $source $name)
            $manifest += @{ path = $name; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
        }
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $studyPath "sources.json") -Encoding utf8
    & git status --short --branch | Set-Content (Join-Path $studyPath "initial-status.txt")
    & git diff --binary | Set-Content (Join-Path $studyPath "initial-dirty.patch")
    & $dotnet --info | Set-Content (Join-Path $studyPath "dotnet-info.txt")
    & $dotnet workload list | Set-Content (Join-Path $studyPath "workloads.txt")
    Write-Output "Frozen $($manifest.Count) source/asset files: $source"
    exit
}
if (($BaselineHeap -or $PrivateLtoCache) -and $Variant -ne "D") { throw "LTO compatibility options are D-only" }
$destination = Join-Path $studyPath $(if ($PrivateLtoCache) { "D-ready" } elseif ($BaselineHeap) { "D-baseline-heap" } else { $Variant })
if (Test-Path -LiteralPath $destination) { throw "Variant already exists; no artifact reuse or overwrite" }
New-Item -ItemType Directory -Path $destination | Out-Null
$env:TEMP = Join-Path $destination "tmp"
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Path $env:TEMP | Out-Null
foreach ($entry in Get-Content (Join-Path $studyPath "sources.json") -Raw | ConvertFrom-Json) {
    $file = Join-Path (Join-Path $studyPath "source") $entry.path
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Frozen source changed: $($entry.path)"
    }
}
$options = @("-p:NuGetAudit=false", "-p:EmccVerbose=true")
if ($Variant -in @("B", "C", "D")) { $options += "-p:EmccLinkOptimizationFlag=-O3" }
if ($Variant -in @("C", "D")) { $options += "-p:EmccCompileOptimizationFlag=-O3" }
if ($Variant -eq "D") {
    $options += @("-p:EmccExtraCFlags=-flto", "-p:EmccExtraBitcodeLDFlags=-flto", "-p:EmccExtraLDFlags=-flto")
    if ($BaselineHeap -or $PrivateLtoCache) {
        $baselineRsp = Get-Content (Join-Path $studyPath "A/build/obj/SpaceBattle.Web/release/wasm/for-publish/emcc-link.rsp") -Raw
        $match = [regex]::Match($baselineRsp, '-s INITIAL_MEMORY=(\d+)')
        if (-not $match.Success) { throw "Baseline initial heap evidence is missing" }
        $options += "-p:EmccInitialHeapSize=$($match.Groups[1].Value)"
    }
    if ($PrivateLtoCache) {
        $cacheEvidence = Get-Content (Join-Path $studyPath "D-cache-build.json") -Raw | ConvertFrom-Json
        if ($cacheEvidence.exitCode -ne 0) { throw "Private LTO cache was not built successfully" }
        $options += "-p:WasmCachePath=$(Join-Path $studyPath 'D-private-cache')"
    }
}
if ($Variant -eq "Profile") { $options += @("-p:WasmNativeStrip=false", "-p:WasmEmitSymbolMap=true") }
$args = @("publish", "SpaceBattle.Web.csproj", "-c", "Release", "--artifacts-path", (Join-Path $destination "build"), "-o", (Join-Path $destination "publish"), "-v:normal", "-bl:$(Join-Path $destination 'build.binlog')", "-flp:logfile=$(Join-Path $destination 'build-diagnostic.log');verbosity=diagnostic") + $options
@{ variant = $Variant; sdk = $dotnet; arguments = $args; startedUtc = [DateTime]::UtcNow.ToString("o"); sourceManifestSha256 = (Get-FileHash (Join-Path $studyPath "sources.json")).Hash } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $destination "invocation.json")
Push-Location (Join-Path $studyPath "source/SpaceBattle.Web")
try {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & $dotnet @args *> (Join-Path $destination "build-console.log")
    $exitCode = $LASTEXITCODE
    $timer.Stop()
    @{ exitCode = $exitCode; elapsedSeconds = $timer.Elapsed.TotalSeconds; finishedUtc = [DateTime]::UtcNow.ToString("o") } | ConvertTo-Json | Set-Content (Join-Path $destination "completion.json")
    Get-Content (Join-Path $destination "build-console.log") -Tail 12
    if ($exitCode -ne 0) { throw "Variant $Variant failed ($exitCode); evidence retained" }
}
finally { Pop-Location }
