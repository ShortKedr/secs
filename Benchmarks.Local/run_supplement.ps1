param([string]$OutputDirectory = 'docs/benchmarks/current', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$target = [IO.Path]::GetFullPath($OutputDirectory, $root)
if (!$target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must stay inside this project.'
}
$env:DOTNET_CLI_HOME = "$root/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$root/Benchmarks.Local/nuget-packages"
$env:TEMP = "$root/Benchmarks.Local/tmp"
$env:TMP = $env:TEMP
$env:DOTNET_TieredCompilation = '0'
$env:SECS_STRESS_EXTREME = '0'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
[IO.Directory]::CreateDirectory($target) | Out-Null
[IO.Directory]::CreateDirectory($env:TEMP) | Out-Null
function Run-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $Arguments" }
}
if (!$SkipBuild) {
    foreach ($project in @('Benchmarks.Local/Comparison.csproj', 'SECS.Benchmarks/SECS.Benchmarks.csproj', 'SECS.OtherBenchmarks/SECS.OtherBenchmarks.csproj')) {
        Run-Dotnet @('build', $project, '-c', 'Release', '--no-restore', '-m:1', '-nr:false', '-p:GeneratePackageOnBuild=false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false')
    }
}
$revision = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot read local revision.' }
$env:SECS_BENCHMARK_REVISION = $revision
$campaignStarted = [DateTimeOffset]::Now.ToString('O')
$sourceHashes = [ordered]@{}
foreach ($file in (git ls-files --cached --others --exclude-standard -- SECS SECS.Benchmarks SECS.OtherBenchmarks Benchmarks.Local | Where-Object { $_ -match '\.(cs|csproj|props|targets)$' } | Sort-Object -Unique)) {
    $sourceHashes[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
}
Run-Dotnet @('Benchmarks.Local/bin/Release/net8.0/Comparison.dll', '--optimized-api', "$target/optimized-api.json")
Run-Dotnet @('Benchmarks.Local/bin/Release/net8.0/Comparison.dll', '--storage', "$target/storage.json")
Run-Dotnet @('SECS.OtherBenchmarks/bin/Release/net8.0/SECS.OtherBenchmarks.dll', '--validate')
$artifacts = Join-Path $env:TEMP ('bdn-' + [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss'))
foreach ($suite in @(
    @{ Name = 'secs'; Project = 'SECS.Benchmarks'; Expected = 34 },
    @{ Name = 'legacy'; Project = 'SECS.OtherBenchmarks'; Expected = 51 }
)) {
    $dll = "$($suite.Project)/bin/Release/net8.0/$($suite.Project).dll"
    $assemblyHash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    $started = [DateTimeOffset]::Now.ToString('O')
    $suiteArtifacts = Join-Path $artifacts $suite.Name
    # One measurement process at a time; Short with eight actual iterations, never Dry.
    Run-Dotnet @($dll, '--filter', '*', '--job', 'Short', '--iterationCount', '8', '--warmupCount', '3', '--inProcess', '--exporters', 'json', '--artifacts', $suiteArtifacts)
    $benchmarks = @()
    $files = @(Get-ChildItem -LiteralPath (Join-Path $suiteArtifacts 'results') -Filter '*-report-full.json' -File | Sort-Object Name)
    foreach ($file in $files) {
        $raw = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $benchmarks += @($raw.Benchmarks)
    }
    if ($benchmarks.Count -ne $suite.Expected) { throw "Incomplete $($suite.Name) BDN matrix: $($benchmarks.Count)/$($suite.Expected)" }
    if ((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -ne $assemblyHash) { throw 'Measured assembly changed.' }
    [ordered]@{
        metadata = [ordered]@{
            sourceRevision = $revision; configuration = 'Release'; tieredCompilation = '0'
            job = 'Short'; toolchain = 'InProcess'; warmupCount = 3; iterationCount = 8
            runtime = $raw.HostEnvironmentInfo.RuntimeVersion; assemblySha256 = $assemblyHash
            sourceHashes = $sourceHashes
            started = $started; finished = [DateTimeOffset]::Now.ToString('O')
            expectedCases = $suite.Expected
            command = "dotnet $dll --filter '*' --job Short --iterationCount 8 --warmupCount 3 --inProcess --exporters json"
            note = 'Collected all eight actual iterations; BenchmarkDotNet statistics retain their configured outlier policy. InProcess avoids generated child-project restore. Extreme five-million-entity stress is opt-in and outside the default matrix.'
        }
        Benchmarks = $benchmarks
    } | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath (Join-Path $target "bdn-$($suite.Name).json") -Encoding utf8
}
foreach ($file in $sourceHashes.Keys) {
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $sourceHashes[$file]) {
        throw "Measured source changed: $file"
    }
}
$inputs = [ordered]@{}
foreach ($name in @('optimized-api.json', 'storage.json', 'bdn-secs.json', 'bdn-legacy.json')) {
    $inputs[$name] = (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash
}
[ordered]@{
    completed = $true; started = $campaignStarted; finished = [DateTimeOffset]::Now.ToString('O')
    sourceRevision = $revision; sourceHashes = $sourceHashes; inputHashes = $inputs
    comparisonAssemblySha256 = (Get-FileHash -LiteralPath 'Benchmarks.Local/bin/Release/net8.0/Comparison.dll' -Algorithm SHA256).Hash
    command = 'powershell -File Benchmarks.Local/run_supplement.ps1'
} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $target 'supplement.json') -Encoding utf8
