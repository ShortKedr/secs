param([string]$OutputDirectory = 'docs/benchmarks/current', [switch]$SkipBuild, [string]$ResumeArtifacts)
$ErrorActionPreference = 'Stop'
if ($ResumeArtifacts -and !$SkipBuild) { throw 'Resume original binaries with -SkipBuild, or start a fresh campaign.' }
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
$env:SKECS_STRESS_EXTREME = '0'
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
    foreach ($project in @('Benchmarks.Local/Comparison.csproj', 'SKECS.Benchmarks/SKECS.Benchmarks.csproj', 'SKECS.OtherBenchmarks/SKECS.OtherBenchmarks.csproj')) {
        Run-Dotnet @('build', $project, '-c', 'Release', '--no-restore', '-m:1', '-nr:false', '-p:GeneratePackageOnBuild=false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false')
    }
}
$revision = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot read local revision.' }
$env:SKECS_BENCHMARK_REVISION = $revision
$campaignStarted = [DateTimeOffset]::Now.ToString('O')
$sourceHashes = [ordered]@{}
foreach ($file in (git ls-files --cached --others --exclude-standard -- SKECS SKECS.Benchmarks SKECS.OtherBenchmarks Benchmarks.Local | Where-Object { $_ -match '\.(cs|csproj|props|targets)$' } | Sort-Object -Unique)) {
    $sourceHashes[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
}
if (!$ResumeArtifacts) {
    Run-Dotnet @('Benchmarks.Local/bin/Release/net8.0/Comparison.dll', '--optimized-api', "$target/optimized-api.json")
    Run-Dotnet @('Benchmarks.Local/bin/Release/net8.0/Comparison.dll', '--storage', "$target/storage.json")
} elseif (!(Test-Path -LiteralPath "$target/optimized-api.json") -or !(Test-Path -LiteralPath "$target/storage.json")) {
    throw 'Resuming requires the original API and storage inputs.'
}
Run-Dotnet @('SKECS.OtherBenchmarks/bin/Release/net8.0/SKECS.OtherBenchmarks.dll', '--validate')
$artifacts = Join-Path $env:TEMP ('bdn-' + [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss'))
if ($ResumeArtifacts) {
    $artifacts = [IO.Path]::GetFullPath($ResumeArtifacts, $root)
    if (!$artifacts.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $artifacts -PathType Container)) {
        throw 'ResumeArtifacts must be an existing directory inside this project.'
    }
}
$snapshotPath = Join-Path $artifacts 'source-snapshot.json'
$assemblyHashes = [ordered]@{}
foreach ($dll in @('Benchmarks.Local/bin/Release/net8.0/Comparison.dll', 'Benchmarks.Local/bin/Release/net8.0/SKECS.dll', 'SKECS.Benchmarks/bin/Release/net8.0/SKECS.Benchmarks.dll', 'SKECS.OtherBenchmarks/bin/Release/net8.0/SKECS.OtherBenchmarks.dll')) {
    $assemblyHashes[$dll] = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
}
if ($ResumeArtifacts) {
    if (!(Test-Path -LiteralPath $snapshotPath)) { throw 'Resume requires the original source snapshot; start a fresh campaign for unrecorded artifacts.' }
    $snapshot = Get-Content -LiteralPath $snapshotPath -Raw | ConvertFrom-Json
    if (($snapshot.sourceHashes | ConvertTo-Json -Depth 20 -Compress) -cne ($sourceHashes | ConvertTo-Json -Depth 20 -Compress) -or
        ($snapshot.assemblyHashes | ConvertTo-Json -Compress) -cne ($assemblyHashes | ConvertTo-Json -Compress)) {
        throw 'Sources or measured binaries changed; do not reuse these artifacts.'
    }
    $revision = $snapshot.sourceRevision
} else {
    [IO.Directory]::CreateDirectory($artifacts) | Out-Null
    @{ sourceRevision = $revision; sourceHashes = $sourceHashes; assemblyHashes = $assemblyHashes } |
        ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $snapshotPath -Encoding utf8
}
foreach ($suite in @(
    @{ Name = 'skecs'; Project = 'SKECS.Benchmarks'; Expected = 34 },
    @{ Name = 'legacy'; Project = 'SKECS.OtherBenchmarks'; Expected = 51 }
)) {
    $dll = "$($suite.Project)/bin/Release/net8.0/$($suite.Project).dll"
    $assemblyHash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    $started = [DateTimeOffset]::Now.ToString('O')
    $suiteArtifacts = Join-Path $artifacts $suite.Name
    # One measurement process at a time; Short with eight actual iterations, never Dry.
    $cachedFiles = @(Get-ChildItem -LiteralPath (Join-Path $suiteArtifacts 'results') -Filter '*-report-full*.json' -File -ErrorAction SilentlyContinue)
    $cachedCases = @($cachedFiles | ForEach-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).Benchmarks })
    $reused = $ResumeArtifacts -and $cachedCases.Count -eq $suite.Expected
    if (!$reused) {
        Run-Dotnet @($dll, '--filter', '*', '--job', 'Short', '--iterationCount', '8', '--warmupCount', '3', '--inProcess', '--exporters', 'json', '--artifacts', $suiteArtifacts)
    }
    $benchmarks = @()
    $files = @(Get-ChildItem -LiteralPath (Join-Path $suiteArtifacts 'results') -Filter '*-report-full*.json' -File | Sort-Object Name)
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
            started = $(if ($reused) { $null } else { $started }); finished = [DateTimeOffset]::Now.ToString('O')
            aggregationStarted = $started
            recoveredExistingMeasurements = [bool]$reused
            artifactModifiedAt = @($files | ForEach-Object { $_.LastWriteTime.ToString('O') })
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
foreach ($name in @('optimized-api.json', 'storage.json', 'bdn-skecs.json', 'bdn-legacy.json')) {
    $inputs[$name] = (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash
}
[ordered]@{
    completed = $true; started = $campaignStarted; finished = [DateTimeOffset]::Now.ToString('O')
    sourceRevision = $revision; sourceHashes = $sourceHashes; inputHashes = $inputs
    comparisonAssemblySha256 = (Get-FileHash -LiteralPath 'Benchmarks.Local/bin/Release/net8.0/Comparison.dll' -Algorithm SHA256).Hash
    command = 'powershell -File Benchmarks.Local/run_supplement.ps1'
} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $target 'supplement.json') -Encoding utf8
