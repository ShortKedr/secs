param([int]$Ticks = 108000, [int]$Warmup = 600, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $root
$env:DOTNET_CLI_HOME = Join-Path $root 'Benchmarks.Local/.dotnet'
$env:NUGET_PACKAGES = Join-Path $root 'Benchmarks.Local/nuget-packages'
$env:DOTNET_TieredCompilation = '0'
if (-not $SkipBuild) {
    dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --no-restore -m:1 -p:GeneratePackageOnBuild=false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
}
$dll = Join-Path $root 'SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll'
$profiles = @(
    @{ Name = 'low'; Churn = '0.5'; Frameworks = @('secs','lite','classic','morpeh','static') },
    @{ Name = 'nominal'; Churn = '1'; Frameworks = @('classic','morpeh','static','secs','lite') },
    @{ Name = 'high'; Churn = '2'; Frameworks = @('static','secs','lite','classic','morpeh') }
)
foreach ($profile in $profiles) {
    $baseline = $null
    foreach ($framework in $profile.Frameworks) {
        $output = "docs/space-battle/results/$($profile.Name)-$framework.json"
        Write-Host "Starting $($profile.Name) / $framework : $Ticks ticks"
        dotnet $dll --framework $framework --ticks $Ticks --warmup $Warmup --seed 7331 --ships-per-team 96 --churn $profile.Churn --output $output
        if ($LASTEXITCODE -ne 0) { throw "Simulation failed: $output" }
        $result = Get-Content -Raw -LiteralPath $output | ConvertFrom-Json
        $signature = @{
            Checkpoints = $result.Checkpoints
            Checksum = $result.Checksum
            Counters = $result.Counters
        } | ConvertTo-Json -Depth 10 -Compress
        if ($null -eq $baseline) { $baseline = $signature }
        elseif ($signature -cne $baseline) { throw "Port divergence: $output" }
        Write-Host "Verified $($profile.Name) / $framework : canonical state and counters match"
    }
}
Write-Host 'Suite complete: all five native ECS ports match within each churn profile.'
