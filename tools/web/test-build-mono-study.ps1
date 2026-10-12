$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
foreach ($name in @("tools/build-space-battle-web.ps1", "tools/web/build-mono-study.ps1", "tools/web/prepare-mono-lto-cache.ps1")) {
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $root $name), [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count) { throw "Parse failed: $name $errors" }
}
function CheckGuard($argsTable, $expected) {
    $failure = $null
    try { & (Join-Path $root "tools/build-space-battle-web.ps1") @argsTable | Out-Null }
    catch { $failure = $_.Exception.Message }
    if (-not $failure -or $failure -notmatch $expected) { throw "Expected $expected; got $failure" }
}
CheckGuard @{ OutputDirectory = "../outside" } "OutputDirectory must remain"
CheckGuard @{ ArtifactsDirectory = "../outside" } "ArtifactsDirectory must remain"
CheckGuard @{ OutputDirectory = "Benchmarks.Local/tmp/same"; ArtifactsDirectory = "Benchmarks.Local/tmp/same" } "must be separate"
CheckGuard @{ OutputDirectory = "Benchmarks.Local/tmp/same"; ArtifactsDirectory = "Benchmarks.Local/tmp/same/build" } "must be separate"
CheckGuard @{ BasePath = "not-absolute" } "BasePath must be"
Write-Output "3 PowerShell scripts parse; 5 pre-build path/base guards pass (no build launched)."
