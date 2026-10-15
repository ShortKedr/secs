param(
    [string]$OutputDirectory = "Benchmarks.Local/tmp/web-publish",
    [string]$BasePath = "/skecs/",
    [switch]$Interpreter,
    [string]$ArtifactsDirectory = "Benchmarks.Local/tmp/web-build",
    [string]$NodeExecutable = "node"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$artifacts = [IO.Path]::GetFullPath((Join-Path $root $ArtifactsDirectory))
if (-not $output.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must remain inside the project."
}
if (-not $artifacts.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "ArtifactsDirectory must remain inside the project."
}
if ($artifacts -eq $output -or $artifacts.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish and intermediate artifact directories must be separate."
}
if ($BasePath -notmatch '^/([a-zA-Z0-9_-]+/)*$') { throw "BasePath must be an absolute URL directory, e.g. /skecs/ or /." }
if (Test-Path -LiteralPath (Join-Path $output "wwwroot/build-identity.json")) {
    throw "OutputDirectory already contains a finalized site. Choose a fresh directory to preserve earlier builds."
}
$env:DOTNET_CLI_HOME = Join-Path $root "Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = Join-Path $root "Benchmarks.Local/nuget-packages"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = "false"
Push-Location (Join-Path $root "SpaceBattle.Web")
try {
    $options = @()
    if ($Interpreter) {
        $options = @("-p:RunAOTCompilation=false", "-p:WasmBuildNative=false")
        Write-Warning "Interpreter build is for local checks only; Pages must use AOT."
    }
    & dotnet publish SpaceBattle.Web.csproj -c Release --artifacts-path $artifacts -o $output @options
    if ($LASTEXITCODE -ne 0) { throw "WebAssembly publish failed ($LASTEXITCODE)." }
    $site = Join-Path $output "wwwroot"
    foreach ($name in @("warp-atlas.png", "warp-atlas.json")) {
        $source = Join-Path $root "SpaceBattle.Game/Assets/$name"
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing shared warp asset: $source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $site "assets/$name") -Force
    }
    $index = Join-Path $site "index.html"
    $html = [IO.File]::ReadAllText($index)
    if ($html -notmatch '<base href="[^"]*"\s*/?>') { throw "Missing base URL in index.html." }
    $html = [Text.RegularExpressions.Regex]::Replace($html, '<base href="[^"]*"\s*/?>', '<base href="' + $BasePath + '">')
    [IO.File]::WriteAllText($index, $html, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $site ".nojekyll"), "", [Text.UTF8Encoding]::new($false))
    & $NodeExecutable (Join-Path $root "tools/web/prepare-web-atlases.mjs") --output (Join-Path $site "assets")
    if ($LASTEXITCODE -ne 0) { throw "Offline web atlas preparation failed ($LASTEXITCODE). Install tools/web dependencies with npm ci." }
    & $NodeExecutable (Join-Path $root "tools/web/finalize-web-site.mjs") --site $site
    if ($LASTEXITCODE -ne 0) { throw "Web build identity finalization failed ($LASTEXITCODE)." }
    if (-not (Test-Path (Join-Path $site "_framework/blazor.boot.json"))) { throw "Missing WASM runtime manifest." }
    if (-not (Test-Path (Join-Path $site "assets/ships-atlas.png"))) { throw "Missing shared ship atlas." }
    if (-not (Test-Path (Join-Path $site "assets/guns-atlas.png")) -or -not (Test-Path (Join-Path $site "assets/guns-atlas.json"))) { throw "Missing shared gun atlas or manifest." }
    if (-not (Test-Path (Join-Path $site "assets/effects-atlas.png")) -or -not (Test-Path (Join-Path $site "assets/effects-atlas.json"))) { throw "Missing shared effects atlas or manifest." }
    if (-not (Test-Path (Join-Path $site "assets/warp-atlas.png")) -or -not (Test-Path (Join-Path $site "assets/warp-atlas.json"))) { throw "Missing shared warp atlas or manifest." }
    Write-Output "Published Space Battle: $site (base $BasePath)"
}
finally { Pop-Location }
