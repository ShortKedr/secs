param(
    [int]$Ticks = 108000,
    [int]$Warmup = 600,
    [ValidateSet('benchmark', 'game')][string]$Preset = 'benchmark',
    [string]$OutputDirectory = 'docs/space-battle/current',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $root
$env:DOTNET_CLI_HOME = Join-Path $root 'Benchmarks.Local/.dotnet'
$env:NUGET_PACKAGES = Join-Path $root 'Benchmarks.Local/nuget-packages'
$env:DOTNET_TieredCompilation = '0'
$env:TEMP = Join-Path $root 'Benchmarks.Local/tmp'
$env:TMP = $env:TEMP
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
[IO.Directory]::CreateDirectory($env:TEMP) | Out-Null
$target = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if (-not $target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Results must stay inside the current project.'
}
if ($Ticks -lt 1 -or $Warmup -lt 0) { throw 'Ticks must be positive and warmup nonnegative.' }
[IO.Directory]::CreateDirectory($target) | Out-Null
if (-not $SkipBuild) {
    dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --no-restore -m:1 -nr:false -p:GeneratePackageOnBuild=false -p:UseSharedCompilation=false -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
}
$dll = Join-Path $root 'SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll'
function Get-SourceHashes {
    $hashes = [ordered]@{}
    $files = git ls-files --cached --others --exclude-standard -- SECS SpaceBattle.Core SpaceBattle.SECS SpaceBattle.Adapters SpaceBattle.Headless Benchmarks.Local/vendor Benchmarks.Local/StaticLibrary.csproj Benchmarks.Local/MorpehLibrary.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify measured source files.' }
    foreach ($file in ($files | Where-Object { $_ -match '\.(cs|csproj|props|targets|json)$' } | Sort-Object -Unique)) {
        $hashes[$file] = (Get-FileHash -LiteralPath (Join-Path $root $file) -Algorithm SHA256).Hash
    }
    return $hashes
}
$sourceHashes = Get-SourceHashes
$manifest = [ordered]@{
    Format = 1
    Completed = $false
    StartedAt = [DateTimeOffset]::Now.ToString('O')
    SourceRevision = (git rev-parse HEAD)
    SourceHashes = $sourceHashes
    Ticks = $Ticks
    WarmupTicks = $Warmup
    TieredCompilation = '0'
    Preset = $Preset
    Command = "tools/space-battle/run-suite.ps1 -Ticks $Ticks -Warmup $Warmup -Preset $Preset -OutputDirectory $OutputDirectory"
    CsvPolicy = 'Tick CSVs are validated locally; their SHA256 hashes are published in manifest.json, not another copy of all raw tick rows.'
}
$manifestPath = Join-Path $target 'manifest.json'
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
& (Join-Path $PSScriptRoot 'capture-hardware.ps1') -Output (Join-Path $OutputDirectory 'hardware.json')
$profiles = @(
    @{ Name = 'low'; Churn = '0.5'; Frameworks = @('secs','lite','classic','morpeh','static') },
    @{ Name = 'nominal'; Churn = '1'; Frameworks = @('classic','morpeh','static','secs','lite') },
    @{ Name = 'high'; Churn = '2'; Frameworks = @('static','secs','lite','classic','morpeh') }
)
foreach ($profile in $profiles) {
    $baseline = $null
    foreach ($framework in $profile.Frameworks) {
        $output = Join-Path $target "$($profile.Name)-$framework.json"
        Write-Host "Starting $($profile.Name) / $framework : $Ticks ticks"
        dotnet $dll --framework $framework --ticks $Ticks --warmup $Warmup --preset $Preset --seed 7331 --ships-per-team 96 --churn $profile.Churn --output $output
        if ($LASTEXITCODE -ne 0) { throw "Simulation failed: $output" }
        $result = Get-Content -Raw -LiteralPath $output | ConvertFrom-Json
        $signature = @{
            Checkpoints = $result.Checkpoints
            Checksum = $result.Checksum
            Counters = $result.Counters
            Tactics = $result.Tactics
            Windows = @($result.Windows | Select-Object StartTick, EndTick, Ships, Projectiles, Debris, Particles, Turrets, Counters)
            HudWindows = @($result.HudWindows | Select-Object StartTick, EndTick, MeanShips, MeanProjectiles, MeanDebris, MeanParticles, MeanTurrets)
        } | ConvertTo-Json -Depth 10 -Compress
        if ($null -eq $baseline) { $baseline = $signature }
        elseif ($signature -cne $baseline) { throw "Port divergence: $output" }
        Write-Host "Verified $($profile.Name) / $framework : canonical state and counters match"
    }
}
$finalHashes = Get-SourceHashes
if (($sourceHashes | ConvertTo-Json -Compress) -cne ($finalHashes | ConvertTo-Json -Compress)) {
    throw 'Measured source files changed during the campaign.'
}
$manifest.Completed = $true
$manifest['FinishedAt'] = [DateTimeOffset]::Now.ToString('O')
$manifest['Files'] = [ordered]@{}
foreach ($file in (Get-ChildItem -LiteralPath $target -File | Where-Object { $_.Name -ne 'manifest.json' -and ($_.Name -eq 'hardware.json' -or $_.Name -match '^(low|nominal|high)-(secs|lite|classic|morpeh|static)\.(json|ticks\.csv)$') } | Sort-Object Name)) {
    $manifest.Files[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host 'Suite complete: all five native ECS ports match within each churn profile.'
