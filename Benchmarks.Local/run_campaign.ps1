param([string]$OutputDirectory = 'docs/benchmarks/current', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory, $root)
if (!$OutputDirectory.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be inside the current project.'
}
$env:DOTNET_CLI_HOME = "$root/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$root/Benchmarks.Local/nuget-packages"
$env:TEMP = "$root/Benchmarks.Local/tmp"
$env:TMP = $env:TEMP
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:SECS_BENCHMARK_SCENARIOS = $null
New-Item -ItemType Directory -Force $OutputDirectory, $env:TEMP | Out-Null
function Run-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $Arguments" }
}
if (!$SkipBuild) {
    Run-Dotnet @('build', 'Benchmarks.Local/Comparison.csproj', '-c', 'Release', '--no-restore', '-p:GeneratePackageOnBuild=false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false', '-m:1', '-nr:false', '--disable-build-servers')
}
$dll = 'Benchmarks.Local/bin/Release/net8.0/Comparison.dll'
Run-Dotnet @($dll, '--validate')
Run-Dotnet @($dll, '--validate-stress')
$revision = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot read local source revision' }
$env:SECS_BENCHMARK_REVISION = $revision
$sources = @{}
$sourceFiles = @(Get-ChildItem SECS -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
$sourceFiles += @(Get-ChildItem SECS.Benchmarks -File -Filter '*.cs')
$sourceFiles += @(Get-ChildItem Benchmarks.Local -File -Filter '*.cs')
$sourceFiles += Get-Item Benchmarks.Local/vendor-manifest.json
$sourceFiles += @(Get-ChildItem Benchmarks.Local -File -Filter '*.csproj')
$sourceFiles += Get-Item Benchmarks.Local/Directory.Build.props, SECS/SECS.csproj
foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
    $sources[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$cpu = (Get-ItemProperty -LiteralPath 'HKLM:/HARDWARE/DESCRIPTION/System/CentralProcessor/0' -Name ProcessorNameString).ProcessorNameString.Trim()
$started = [DateTimeOffset]::Now.ToString('O')
$assemblyBefore = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant()
$secsBefore = (Get-FileHash 'Benchmarks.Local/bin/Release/net8.0/SECS.dll' -Algorithm SHA256).Hash.ToLowerInvariant()
# One workload process at a time. Do not run games, builds or other benchmarks concurrently.
Run-Dotnet @($dll, "$OutputDirectory/base.json")
Run-Dotnet @($dll, '--stress', "$OutputDirectory/stress.json")
foreach ($relative in $sources.Keys) {
    $after = (Get-FileHash -LiteralPath (Join-Path $root $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($after -ne $sources[$relative]) { throw "Source changed during measurement: $relative" }
}
if ((Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $assemblyBefore -or
    (Get-FileHash 'Benchmarks.Local/bin/Release/net8.0/SECS.dll' -Algorithm SHA256).Hash.ToLowerInvariant() -ne $secsBefore) {
    throw 'Measured assemblies changed during measurement.'
}
$manifest = [ordered]@{
    format = 1; started = $started; finished = [DateTimeOffset]::Now.ToString('O')
    sourceRevision = $revision; sourceHashes = $sources
    cpu = $cpu; logicalProcessors = [Environment]::ProcessorCount
    sdk = (& dotnet --version | Out-String).Trim()
    measurementAssemblySha256 = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant()
    secsAssemblySha256 = (Get-FileHash 'Benchmarks.Local/bin/Release/net8.0/SECS.dll' -Algorithm SHA256).Hash.ToLowerInvariant()
    baseInputSha256 = (Get-FileHash "$OutputDirectory/base.json" -Algorithm SHA256).Hash.ToLowerInvariant()
    stressInputSha256 = (Get-FileHash "$OutputDirectory/stress.json" -Algorithm SHA256).Hash.ToLowerInvariant()
    matrix = @{ base = 40; stress = 70; frameworks = 5 }
    methodology = @{ baseSamples = 20; stressSamples = 8; seed = 2048; singleThread = $true; caseOrder = 'deterministically shuffled within each suite'; tiering = 'off' }
    validation = @{ base = 'passed before timing'; stress = 'passed before timing and after each stress case' }
    command = 'powershell -File Benchmarks.Local/run_campaign.ps1'
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content "$OutputDirectory/campaign.json" -Encoding utf8
