param(
    [Parameter(Mandatory = $true)][string] $UnityZip,
    [Parameter(Mandatory = $true)][string] $UnityEditor,
    [ValidateSet('Editor', 'Mono', 'IL2CPP')][string] $Backend = 'Editor',
    [string] $Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$zip = (Resolve-Path -LiteralPath $UnityZip).Path
$editor = (Resolve-Path -LiteralPath $UnityEditor).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$scratch = Join-Path $repoRoot ('artifacts/release-consumers/unity-' + [guid]::NewGuid().ToString('N'))
$project = Join-Path $scratch 'project'
$expandedZip = Join-Path $scratch 'zip'
New-Item -ItemType Directory -Path $project, $expandedZip -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Unity/Assets'), (Join-Path $PSScriptRoot 'Unity/Packages'), (Join-Path $PSScriptRoot 'Unity/ProjectSettings') -Destination $project -Recurse
Expand-Archive -LiteralPath $zip -DestinationPath $expandedZip
$library = @(Get-ChildItem -LiteralPath $expandedZip -Filter 'SECS.dll' -File -Recurse)
if ($library.Count -ne 1) { throw "Expected exactly one SECS.dll in Unity ZIP; found $($library.Count)." }
$numericVersion = ($Version -split '[-+]')[0]
if ($numericVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid package version: $Version" }
$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($library[0].FullName).Version.ToString(3)
if ($assemblyVersion -ne $numericVersion) {
    throw "Unity ZIP SECS.dll assembly version $assemblyVersion does not match package version $Version."
}
$plugins = Join-Path $project 'Assets/Plugins'
New-Item -ItemType Directory -Path $plugins | Out-Null
Get-ChildItem -LiteralPath $library[0].DirectoryName -Filter '*.dll' -File | Copy-Item -Destination $plugins

$method = switch ($Backend) {
    'Editor' { 'SECSReleaseSmokeEditor.RunEditor' }
    'Mono' { 'SECSReleaseSmokeEditor.BuildMono' }
    'IL2CPP' { 'SECSReleaseSmokeEditor.BuildIl2Cpp' }
}
$editorLog = Join-Path $scratch 'editor.log'
$editorProcess = Start-Process -FilePath $editor -ArgumentList @(
    '-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', $method, '-logFile', ('"' + $editorLog + '"')
) -PassThru -WindowStyle Hidden
if (-not $editorProcess.WaitForExit(600000)) {
    throw "Unity Editor timed out; process $($editorProcess.Id), log: $editorLog"
}
$editorExitCode = $editorProcess.ExitCode
if ($editorExitCode -ne 0) {
    if (Test-Path -LiteralPath $editorLog) { Get-Content -LiteralPath $editorLog -Tail 100 }
    throw "Unity Editor failed with code $editorExitCode; log: $editorLog"
}
$marker = if ($Backend -eq 'Editor') { 'SECS_UNITY_EDITOR_OK' } else { "SECS_UNITY_BUILD_OK $Backend" }
if (-not (Select-String -LiteralPath $editorLog -SimpleMatch $marker -Quiet)) {
    Get-Content -LiteralPath $editorLog -Tail 100
    throw "Missing $marker; log: $editorLog"
}
Write-Output "PASS: $marker; log: $editorLog"
if ($Backend -eq 'Editor') { return }

$player = Join-Path $project ("Builds/$Backend/SECSReleaseSmoke.exe")
if (-not (Test-Path -LiteralPath $player)) { throw "Missing built Unity player: $player" }
$playerLog = Join-Path $scratch 'player.log'
$playerProcess = Start-Process -FilePath $player -ArgumentList @(
    '-batchmode', '-nographics', '-logFile', ('"' + $playerLog + '"')
) -PassThru -WindowStyle Hidden
if (-not $playerProcess.WaitForExit(180000)) {
    throw "Unity $Backend player timed out; process $($playerProcess.Id), log: $playerLog"
}
$playerExitCode = $playerProcess.ExitCode
if ($playerExitCode -ne 0 -or -not (Select-String -LiteralPath $playerLog -SimpleMatch 'SECS_UNITY_OK' -Quiet)) {
    if (Test-Path -LiteralPath $playerLog) { Get-Content -LiteralPath $playerLog -Tail 100 }
    throw "Unity $Backend player smoke failed with code $playerExitCode; log: $playerLog"
}
Write-Output "PASS: SECS_UNITY_OK ($Backend); log: $playerLog"
