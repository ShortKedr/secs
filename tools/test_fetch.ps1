$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temporary = Join-Path $root "Benchmarks.Local/tmp/fetch-check-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($temporary) | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Benchmarks.Local/fetch.ps1') -Destination $temporary
$manifest = Get-Content -LiteralPath (Join-Path $root 'Benchmarks.Local/vendor-manifest.json') -Raw | ConvertFrom-Json
$component = $manifest.components | Where-Object id -eq 'ecslite'
# A synthetic pinned-layout archive tests recovery offline, using included sources.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open((Join-Path $temporary 'ecslite.zip'), [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $component.files) {
        $source = Join-Path $root "Benchmarks.Local/vendor/ecslite/$($file.path)"
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
            throw "Fixture source differs from manifest: $source"
        }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $source, "ecslite-$($component.revision)/$($file.path)") | Out-Null
    }
} finally { $archive.Dispose() }
$fixture = @{ format = 1; components = @($component) } | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText((Join-Path $temporary 'vendor-manifest.json'), $fixture)
& (Join-Path $temporary 'fetch.ps1')
foreach ($file in $component.files) {
    $target = Join-Path $temporary "vendor/ecslite/$($file.path)"
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
        throw "Recovery mismatch: $($file.path)"
    }
}
& (Join-Path $temporary 'fetch.ps1')
$probe = Join-Path $temporary "vendor/ecslite/$($component.files[0].path)"
$corruption = [IO.File]::ReadAllBytes($probe) + [byte]0
[IO.File]::WriteAllBytes($probe, $corruption)
$before = (Get-FileHash -LiteralPath $probe -Algorithm SHA256).Hash
$rejected = $false
try { & (Join-Path $temporary 'fetch.ps1') }
catch {
    if ($_.Exception.Message -notlike 'Existing source differs from pinned manifest*') { throw }
    $rejected = $true
}
if (!$rejected -or (Get-FileHash -LiteralPath $probe -Algorithm SHA256).Hash -ne $before) {
    throw 'Recovery must reject and preserve changed local files.'
}
Write-Output "PASS: offline recovery fixture, repeat verification, and refusal to overwrite local edits; fixture retained at $temporary"
