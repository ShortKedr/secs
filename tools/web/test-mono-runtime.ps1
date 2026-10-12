param([string]$DotnetExecutable = "dotnet")
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$env:DOTNET_CLI_HOME = Join-Path $root "Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = Join-Path $root "Benchmarks.Local/nuget-packages"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = "false"

function CheckRuntime($mono, $readyToRun, $aot, $accepted) {
    $result = & $DotnetExecutable msbuild SpaceBattle.Web.csproj -nologo -verbosity:quiet `
        -target:RequireMonoWebRuntime "-property:UseMonoRuntime=$mono" `
        "-property:PublishReadyToRun=$readyToRun" "-property:RunAOTCompilation=$aot" 2>&1
    $code = $LASTEXITCODE
    if ($accepted -and $code -ne 0) { throw "Supported Mono mode rejected: $result" }
    if (-not $accepted -and ($code -eq 0 -or ($result -join "`n") -notmatch "Space Battle Web supports Mono only")) {
        throw "Unsupported runtime was not rejected by the intended guard: $result"
    }
}

Push-Location (Join-Path $root "SpaceBattle.Web")
try {
    CheckRuntime "true" "false" "true" $true
    CheckRuntime "true" "false" "false" $true
    CheckRuntime "false" "false" "true" $false
    CheckRuntime "true" "true" "true" $false
    Write-Output "PASS: production/local Mono accepted; non-Mono and ReadyToRun rejected before compilation."
    $global:LASTEXITCODE = 0
}
finally { Pop-Location }
