param(
    [string]$WorkDirectory = 'Benchmarks.Local/tmp/scene-tools',
    [string]$DotnetExecutable = '',
    [string]$NodeExecutable = 'node'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $root
try {
    $prepared = & $NodeExecutable tools/space-battle/scenes/prepare-scenes.mjs --phase init --work $WorkDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Scene preparation failed. Use a fresh WorkDirectory after source changes or an interrupted build.' }
    $state = ($prepared | Select-Object -Last 1) | ConvertFrom-Json
    if ($state.reused) { Write-Output "Reusing verified builds: $($state.prepared)"; return }
    $work = $state.work
    if (-not $DotnetExecutable) {
        $local = Join-Path $root 'Benchmarks.Local/tmp/dotnet-aot/dotnet.exe'
        $DotnetExecutable = if (Test-Path -LiteralPath $local) { $local } else { (Get-Command dotnet -ErrorAction Stop).Source }
    }
    $DotnetExecutable = (Get-Command $DotnetExecutable -ErrorAction Stop).Source
    $env:DOTNET_ROOT = Split-Path -Parent $DotnetExecutable
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $env:PATH = $env:DOTNET_ROOT + [IO.Path]::PathSeparator + $env:PATH
    $env:DOTNET_CLI_HOME = Join-Path $work 'cache/dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $root 'Benchmarks.Local/nuget-packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    $env:UseSharedCompilation = 'false'
    $env:TEMP = $env:TMP = Join-Path $work 'cache/tmp'
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
    $sdk = ''
    foreach ($variant in @('production', 'probe')) {
        $source = Join-Path $work "source-$variant"
        $site = Join-Path $work "$variant-site"
        $artifacts = Join-Path $work "$variant-build"
        Write-Output "Building isolated $variant Mono AOT (no publishing)..."
        Push-Location (Join-Path $source 'SpaceBattle.Web')
        try {
            $sdk = & $DotnetExecutable --version
            if ($LASTEXITCODE -ne 0) { throw 'Pinned .NET SDK unavailable.' }
            & $DotnetExecutable publish SpaceBattle.Web.csproj -c Release --artifacts-path $artifacts -o $site --disable-build-servers
            if ($LASTEXITCODE -ne 0) { throw "$variant AOT build failed" }
        } finally { Pop-Location }
        $www = Join-Path $site 'wwwroot'
        $index = Join-Path $www 'index.html'
        $html = [IO.File]::ReadAllText($index)
        if ([regex]::Matches($html, '<base href="[^"]*"\s*/?>').Count -ne 1) { throw 'Expected one base URL in generated index.' }
        $html = [regex]::Replace($html, '<base href="[^"]*"\s*/?>', '<base href="/">')
        [IO.File]::WriteAllText($index, $html, [Text.UTF8Encoding]::new($false))
        & $NodeExecutable tools/web/prepare-web-atlases.mjs --input (Join-Path $source 'SpaceBattle.Game/Assets') --output (Join-Path $www 'assets')
        if ($LASTEXITCODE -ne 0) { throw 'Offline atlas preparation failed' }
        & $NodeExecutable tools/web/finalize-web-site.mjs --site $www
        if ($LASTEXITCODE -ne 0) { throw 'Site finalization failed' }
    }
    & $NodeExecutable tools/space-battle/scenes/prepare-scenes.mjs --phase finish --work $work --sdk $sdk
    if ($LASTEXITCODE -ne 0) { throw 'Build provenance verification failed' }
} finally { Pop-Location }
