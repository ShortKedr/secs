$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bundledGit = Join-Path $HOME '.cache/codex-runtimes/codex-primary-runtime/dependencies/native/git/cmd/git.exe'
$git = if (Test-Path -LiteralPath $bundledGit) { $bundledGit } else { 'git' }
$destination = [IO.Path]::GetFullPath((Join-Path $root "Benchmarks.Local/tmp/source-check-$([Guid]::NewGuid().ToString('N'))"))
if (!$destination.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Verification copy must stay inside the project.'
}

function Invoke-Checked([string[]] $arguments) {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $arguments" }
}

$oldEnvironment = @{}
foreach ($name in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'TEMP', 'TMP', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE')) {
    $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
Push-Location $root
try {
    $files = @(& $git -c core.quotepath=false ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate publishable repository files.' }
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    foreach ($relative in $files) {
        $source = [IO.Path]::GetFullPath((Join-Path $root $relative))
        $target = [IO.Path]::GetFullPath((Join-Path $destination $relative))
        if (!$source.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            !$target.StartsWith($destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Repository path escapes verification scope: $relative"
        }
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        if ($relative -match '\.(dll|pdb|zip|nupkg|exe|so|dylib)$') { throw "Unexpected binary/archive in source distribution: $relative" }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
    $env:DOTNET_CLI_HOME = Join-Path $root 'Benchmarks.Local/.dotnet'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:NUGET_PACKAGES = Join-Path $root 'Benchmarks.Local/nuget-packages'
    $env:TEMP = Join-Path $destination 'Benchmarks.Local/tmp'
    $env:TMP = $env:TEMP
    [IO.Directory]::CreateDirectory($env:TEMP) | Out-Null
    Set-Location $destination
    # Only restore reads may need network; all build/test outputs stay in this copy.
    Invoke-Checked @('build', 'SECS.sln', '-c', 'Release', '-m:1', '--nologo', '-p:NuGetAudit=false', '-p:RestoreIgnoreFailedSources=true')
    Invoke-Checked @('test', 'SECS.Tests/SECS.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--nologo')
    Invoke-Checked @('Benchmarks.Local/bin/Release/net8.0/Comparison.dll', '--validate')
    Invoke-Checked @('Benchmarks.Local/bin/Release/net8.0/Comparison.dll', '--validate-stress')
    Invoke-Checked @('SECS.OtherBenchmarks/bin/Release/net8.0/SECS.OtherBenchmarks.dll', '--validate')
    foreach ($mode in @('--validate-adapters', '--validate', '--validate-ports')) {
        Invoke-Checked @('SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll', $mode)
    }
    Write-Output "PASS: publishable-only source distribution built and validation passed; copy retained at $destination"
} finally {
    Pop-Location
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name], 'Process') }
}
