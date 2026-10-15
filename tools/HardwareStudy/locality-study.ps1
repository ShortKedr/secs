param(
    [Parameter(Mandatory)][string[]]$Assemblies,
    [Parameter(Mandatory)][string[]]$Labels,
    [Parameter(Mandatory)][string]$Output,
    [int]$Repeats = 3,
    [int]$Ticks = 2400,
    [int]$PhaseTicks = 600,
    [uint32]$Seed = 7331,
    [switch]$Scalar
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$local = Join-Path $root 'Benchmarks.Local/tmp'
function LocalPath([string]$value) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $value))
    if (!$path.StartsWith($local + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Artifacts must stay in ignored Benchmarks.Local/tmp: $path"
    }
    return $path
}
if ($Assemblies.Count -lt 2 -or $Assemblies.Count -ne $Labels.Count -or $Repeats -lt 3 -or $Ticks -lt 1 -or $PhaseTicks -lt 1) {
    throw 'Provide matching variants, positive windows and at least three repeats.'
}
if (@($Labels | Sort-Object -Unique).Count -ne $Labels.Count -or @($Labels | Where-Object { $_ -notmatch '^[a-zA-Z0-9_-]+$' }).Count) {
    throw 'Use distinct simple labels.'
}
$binaries = @($Assemblies | ForEach-Object { LocalPath $_ })
foreach ($file in $binaries) { if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing harness: $file" } }
$destination = LocalPath $Output
if (Test-Path -LiteralPath $destination) { throw 'Use a fresh result directory.' }
New-Item -ItemType Directory -Path $destination | Out-Null
$sdk = Join-Path $local 'dotnet-aot'
$env:DOTNET_ROOT = $sdk; $env:DOTNET_ROOT_X64 = $sdk
$env:DOTNET_CLI_HOME = Join-Path $root 'Benchmarks.Local/.dotnet'
$env:NUGET_PACKAGES = Join-Path $root 'Benchmarks.Local/nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:TEMP = $local; $env:TMP = $local
$env:DOTNET_TieredCompilation = '0'; $env:DOTNET_ReadyToRun = '0'
$env:DOTNET_EnableHWIntrinsic = $(if ($Scalar) { '0' } else { '1' })
$plan = @(); $round = 0
foreach ($profile in @(@('low','.5'), @('nominal','1'), @('high','2'))) {
    for ($repeat = 1; $repeat -le $Repeats; $repeat++) {
        for ($position = 0; $position -lt $binaries.Count; $position++) {
            $variant = ($position + $round) % $binaries.Count
            $name = "$($profile[0])-$($Labels[$variant])-r$repeat"
            $file = Join-Path $destination "$name.json"
            $plan += @{profile=$profile[0]; label=$Labels[$variant]; repeat=$repeat; position=$position; output=$file}
            # Each process warms a separate full-length battle and creates a fresh measured one.
            & "$sdk/dotnet.exe" $binaries[$variant] --game --port skecs --ticks $Ticks --phase-ticks $PhaseTicks --repeats 1 --seed $Seed --churn $profile[1] --output $file
            if ($LASTEXITCODE -ne 0) { throw "Replay failed: $name" }
        }
        $round++
    }
}
@{plan=$plan; scalar=$Scalar.IsPresent; ticks=$Ticks; phaseTicks=$PhaseTicks; seed=$Seed; repeats=$Repeats;
  tieredCompilation=$env:DOTNET_TieredCompilation; readyToRun=$env:DOTNET_ReadyToRun;
  binaries=@($binaries | ForEach-Object { @{path=$_; sha256=(Get-FileHash -LiteralPath $_).Hash;
    coreSha256=(Get-FileHash -LiteralPath (Join-Path (Split-Path $_) 'SpaceBattle.Core.dll')).Hash;
    skecsSha256=(Get-FileHash -LiteralPath (Join-Path (Split-Path $_) 'SpaceBattle.SKECS.dll')).Hash} })
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $destination 'manifest.json')
