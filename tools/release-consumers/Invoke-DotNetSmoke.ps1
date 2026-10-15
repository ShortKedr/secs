param(
    [Parameter(Mandatory = $true)][string] $PackageDirectory,
    [ValidateSet('net8.0', 'net10.0', 'net48')][string] $Framework = 'net8.0',
    [string] $Version = '0.2.0',
    [string] $SdkVersion,
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'DotNet/DotNet.csproj'
$feed = (Resolve-Path -LiteralPath $PackageDirectory).Path
$package = Join-Path $feed "ShortKedr.SKECS.$Version.nupkg"
if (-not (Test-Path -LiteralPath $package)) {
    throw "Missing ShortKedr.SKECS.$Version.nupkg in $feed"
}
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$env:NUGET_PACKAGES = Join-Path $repoRoot ('artifacts/release-consumers/nuget-' + [guid]::NewGuid().ToString('N'))
$env:DOTNET_CLI_HOME = $env:NUGET_PACKAGES
New-Item -ItemType Directory -Path $env:NUGET_PACKAGES -Force | Out-Null
$config = Join-Path $env:NUGET_PACKAGES 'NuGet.Config'
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
$extraSource = if ($Framework -eq 'net48' -and -not $Offline) { '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>' } else { '' }
Set-Content -LiteralPath $config -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="release" value="$escapedFeed"/>$extraSource</packageSources></configuration>
"@

if (-not $SdkVersion) {
    $sdkMajor = if ($Framework -eq 'net10.0') { '10' } else { '8' }
    $candidate = dotnet --list-sdks | ForEach-Object { if ($_ -match "^($sdkMajor\.\d+\.\d+)\s") { [version]$Matches[1] } } | Sort-Object -Descending | Select-Object -First 1
    if ($null -ne $candidate) { $SdkVersion = $candidate.ToString() }
}
if (-not $SdkVersion) { throw "No suitable SDK found for $Framework. Supply -SdkVersion to select an installed SDK." }
$sdkSelection = Join-Path $PSScriptRoot 'DotNet/global.json'
if (Test-Path -LiteralPath $sdkSelection) { throw "Temporary SDK selection already exists: $sdkSelection" }
Set-Content -LiteralPath $sdkSelection -Encoding utf8 -Value "{`"sdk`":{`"version`":`"$SdkVersion`"}}"
Push-Location (Join-Path $PSScriptRoot 'DotNet')
try {
    Write-Output "Smoke SDK: $(dotnet --version)"

    dotnet restore $project --configfile $config -p:TargetFramework=$Framework -p:SKECSVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "Package restore failed for $Framework" }
    $restoredPackage = Join-Path $env:NUGET_PACKAGES "shortkedr.skecs/$Version/shortkedr.skecs.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $restoredPackage) -or
        (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $restoredPackage -Algorithm SHA256).Hash) {
        throw 'Restored package does not match the built release package.'
    }
    dotnet build $project -c Release -f $Framework --no-restore -p:SKECSVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "Consumer build failed for $Framework" }

    if ($Framework -eq 'net48') {
        if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') { throw 'The net48 runtime smoke requires Windows.' }
        $app = Join-Path $PSScriptRoot 'DotNet/bin/Release/net48/DotNet.exe'
        & $app
    } else {
        dotnet run --project $project -c Release -f $Framework --no-build --no-restore -p:SKECSVersion=$Version
    }
    if ($LASTEXITCODE -ne 0) { throw "Consumer runtime failed for $Framework" }
} finally {
    Pop-Location
    Remove-Item -LiteralPath $sdkSelection
}
