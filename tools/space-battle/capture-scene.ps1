param(
    [Parameter(Mandatory = $true)][string]$Search,
    [ValidateSet('group', 'heavy', 'warp')][string]$Type = 'heavy',
    [string]$Id = '',
    [int]$Rank = 1,
    [string]$OutputDirectory = '',
    [int]$Width = 1920,
    [int]$Height = 1080,
    [ValidateSet(1, 2)][int]$Slowmo = 1,
    [ValidateSet(0, 1)][int]$Annotate = 1,
    [ValidateSet('jpeg', 'png')][string]$FrameCodec = 'jpeg',
    [string]$Ffmpeg = 'ffmpeg',
    [string]$Ffprobe = 'ffprobe',
    [string]$NodeExecutable = 'node'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $root
try {
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path (Split-Path $Search -Parent) ('video-' + $Type + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) }
    $selection = if ($Id) { @('--id', $Id) } else { @('--type', $Type, '--rank', $Rank) }
    & $NodeExecutable tools/space-battle/scenes/capture-scene.mjs --search $Search @selection --output $OutputDirectory --width $Width --height $Height --fps 60 --slowmo $Slowmo --annotate $Annotate --frame-codec $FrameCodec --ffmpeg $Ffmpeg --ffprobe $Ffprobe
    if ($LASTEXITCODE -ne 0) { throw 'Capture or MP4 verification failed; inspect failure.json in the output directory.' }
} finally { Pop-Location }
