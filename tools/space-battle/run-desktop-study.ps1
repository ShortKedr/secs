param([int]$Ticks = 1200, [string]$OutputDirectory = 'docs/space-battle/desktop-current', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $root
$target = [IO.Path]::GetFullPath($OutputDirectory, $root)
if (!$target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Outputs must stay inside this project.' }
if ($Ticks -lt 1200 -or $Ticks % 600 -ne 0) { throw 'Use at least two complete 600-tick windows.' }
$env:DOTNET_CLI_HOME = "$root/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$root/Benchmarks.Local/nuget-packages"
$env:TEMP = "$root/Benchmarks.Local/tmp"; $env:TMP = $env:TEMP
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
[IO.Directory]::CreateDirectory($target) | Out-Null
function Run-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $Arguments" }
}
if (!$SkipBuild) {
    foreach ($project in @('SpaceBattle.Headless/SpaceBattle.Headless.csproj', 'SpaceBattle.Game/SpaceBattle.Game.csproj')) {
        Run-Dotnet @('build', $project, '-c', 'Release', '--no-restore', '-m:1', '-nr:false', '-p:GeneratePackageOnBuild=false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false')
    }
}
$sources = [ordered]@{}
foreach ($file in (git ls-files --cached --others --exclude-standard -- SKECS SpaceBattle.Core SpaceBattle.SKECS SpaceBattle.Game SpaceBattle.Headless | Where-Object { $_ -match '\.(cs|csproj|props|targets)$' -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Sort-Object -Unique)) {
    $sources[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
}
$manifest = [ordered]@{ Completed = $false; StartedAt = [DateTimeOffset]::Now.ToString('O'); Ticks = $Ticks
    SourceRevision = (git rev-parse HEAD).Trim(); SourceHashes = $sources
    Scope = 'Five independent fresh processes. Game preset nominal. Headless has a separate 600-tick warmup; desktop intentionally records cold startup. Paced desktop is one Step/rendered frame with a 60 Hz cap and VSync off, not live accumulator FPS.' }
$manifestPath = Join-Path $target 'manifest.json'
$manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8
$headless = 'SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll'
$desktop = 'SpaceBattle.Game/bin/Release/net8.0/SpaceBattle.Game.dll'
foreach ($name in @('SpaceBattle.Core.dll', 'SpaceBattle.SKECS.dll', 'SKECS.dll')) {
    if ((Get-FileHash "SpaceBattle.Headless/bin/Release/net8.0/$name").Hash -ne (Get-FileHash "SpaceBattle.Game/bin/Release/net8.0/$name").Hash) {
        throw "Matched study requires identical loaded assemblies: $name"
    }
}
$cases = @(
    @{ Name = 'headless-warm-fast'; Tiering = '0'; Host = 'headless'; Paced = $false },
    @{ Name = 'headless-warm-paced'; Tiering = '0'; Host = 'headless'; Paced = $true },
    @{ Name = 'desktop-cold-fast-tiering0'; Tiering = '0'; Host = 'desktop'; Paced = $false },
    @{ Name = 'desktop-cold-fast-default'; Tiering = $null; Host = 'desktop'; Paced = $false },
    @{ Name = 'desktop-cold-paced-default'; Tiering = $null; Host = 'desktop'; Paced = $true }
)
foreach ($case in $cases) {
    $env:DOTNET_TieredCompilation = $case.Tiering
    $output = Join-Path $target "$($case.Name).json"
    if ($case.Host -eq 'headless') {
        $arguments = @($headless, '--preset', 'game', '--framework', 'skecs', '--ticks', "$Ticks", '--warmup', '600', '--seed', '7331', '--ships-per-team', '96', '--output', $output)
        if ($case.Paced) { $arguments += '--realtime' }
    } else {
        $arguments = @($desktop, '--profile-frames', "$Ticks", '--profile-warmup', '0', '--profile-output', $output, '--profile-csv', (Join-Path $target "$($case.Name).ticks.csv"), '--seed', '7331', '--ships-per-team', '96', '--hidden')
        if ($case.Paced) { $arguments += '--profile-paced' }
    }
    Run-Dotnet $arguments
}
foreach ($file in $sources.Keys) {
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $sources[$file]) { throw "Measured source changed: $file" }
}
$manifest.Completed = $true
$manifest['FinishedAt'] = [DateTimeOffset]::Now.ToString('O')
$manifest['Files'] = [ordered]@{}
$expectedFiles = @($cases | ForEach-Object { "$($_.Name).json"; "$($_.Name).ticks.csv" })
foreach ($file in (Get-ChildItem -LiteralPath $target -File | Where-Object { $_.Name -in $expectedFiles } | Sort-Object Name)) {
    $manifest.Files[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8
