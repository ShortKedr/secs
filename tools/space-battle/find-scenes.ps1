param(
    [string]$WorkDirectory = 'Benchmarks.Local/tmp/scene-tools',
    [string]$OutputDirectory = '',
    [ValidateSet('low', 'nominal', 'high')][string[]]$Profiles = @('nominal', 'high'),
    [uint32[]]$Seeds = @(7331),
    [ValidateRange(1, 18000)][int]$Ticks = 4200,
    [ValidateRange(1, 12)][int]$MaxCandidates = 3,
    [string]$DotnetExecutable = '',
    [string]$NodeExecutable = 'node'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $root
try {
    if ($Seeds.Count -lt 1 -or $Seeds.Count -gt 8 -or $Profiles.Count -lt 1 -or
        $Seeds.Count * $Profiles.Count -gt 12 -or $Seeds.Count * $Profiles.Count * $Ticks -gt 216000 -or
        @($Seeds | Select-Object -Unique).Count -ne $Seeds.Count -or
        @($Profiles | Select-Object -Unique).Count -ne $Profiles.Count) { throw 'Search budget: distinct inputs, at most 8 seeds / 12 runs / 216000 ticks.' }
    & $PSScriptRoot/prepare-scenes.ps1 -WorkDirectory $WorkDirectory -DotnetExecutable $DotnetExecutable -NodeExecutable $NodeExecutable
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $WorkDirectory ('search-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) }
    & $NodeExecutable tools/space-battle/scenes/find-scenes.mjs --prepared (Join-Path $WorkDirectory 'prepared.json') --output $OutputDirectory --profiles ($Profiles -join ',') --seeds ($Seeds -join ',') --ticks $Ticks --max-candidates $MaxCandidates
    if ($LASTEXITCODE -ne 0) { throw 'Scene search or production replay verification failed' }
} finally { Pop-Location }
