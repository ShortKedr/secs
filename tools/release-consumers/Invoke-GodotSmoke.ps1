param(
    [Parameter(Mandatory = $true)][string] $PackageDirectory,
    [Parameter(Mandatory = $true)][string] $GodotExecutable,
    [string] $Version = '0.2.0',
    [string] $SdkVersion
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Join-Path $PSScriptRoot 'Godot'
$project = Join-Path $projectDirectory 'SKECSReleaseSmoke.csproj'
$feed = (Resolve-Path -LiteralPath $PackageDirectory).Path
$package = Join-Path $feed "ShortKedr.SKECS.$Version.nupkg"
if (-not (Test-Path -LiteralPath $package)) {
    throw "Missing ShortKedr.SKECS.$Version.nupkg in $feed"
}
$godot = (Resolve-Path -LiteralPath $GodotExecutable).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$env:NUGET_PACKAGES = Join-Path $repoRoot ('artifacts/release-consumers/nuget-' + [guid]::NewGuid().ToString('N'))
$env:DOTNET_CLI_HOME = $env:NUGET_PACKAGES
New-Item -ItemType Directory -Path $env:NUGET_PACKAGES -Force | Out-Null
$config = Join-Path $env:NUGET_PACKAGES 'NuGet.Config'
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
Set-Content -LiteralPath $config -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="release" value="$escapedFeed"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
"@

if (-not $SdkVersion) {
    $candidate = dotnet --list-sdks | ForEach-Object { if ($_ -match '^(8\.\d+\.\d+)\s') { [version]$Matches[1] } } | Sort-Object -Descending | Select-Object -First 1
    if ($null -ne $candidate) { $SdkVersion = $candidate.ToString() }
}
if (-not $SdkVersion) { throw 'A .NET 8 SDK is required for the Godot 4.5 smoke.' }
$sdkSelection = Join-Path $projectDirectory 'global.json'
if (Test-Path -LiteralPath $sdkSelection) { throw "Temporary SDK selection already exists: $sdkSelection" }
Set-Content -LiteralPath $sdkSelection -Encoding utf8 -Value "{`"sdk`":{`"version`":`"$SdkVersion`"}}"
Push-Location $projectDirectory
try {
    Write-Output "Godot smoke SDK: $(dotnet --version)"
    dotnet restore $project --configfile $config -p:SKECSVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw 'Godot package restore failed.' }
    $restoredPackage = Join-Path $env:NUGET_PACKAGES "shortkedr.skecs/$Version/shortkedr.skecs.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $restoredPackage) -or
        (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $restoredPackage -Algorithm SHA256).Hash) {
        throw 'Restored Godot package does not match the built release package.'
    }
    dotnet build $project -c Debug --no-restore -p:SKECSVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw 'Godot C# build failed.' }
} finally {
    Pop-Location
    Remove-Item -LiteralPath $sdkSelection
}

$importLog = & $godot --headless --editor --path $projectDirectory --import 2>&1
$importExitCode = $LASTEXITCODE
$importLog | ForEach-Object { Write-Output $_ }
if ($importExitCode -ne 0) { throw "Godot asset import failed (exit code $importExitCode)." }

$log = & $godot --headless --path $projectDirectory --quit-after 120 2>&1
$exitCode = $LASTEXITCODE
$log | ForEach-Object { Write-Output $_ }
if ($exitCode -ne 0 -or -not ($log -match 'SKECS_GODOT_OK')) {
    throw "Godot scene smoke failed (exit code $exitCode)."
}
