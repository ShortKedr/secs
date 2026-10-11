$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'vendor-manifest.json') -Raw | ConvertFrom-Json
$vendorRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'vendor'))

function Get-BytesHash([byte[]] $bytes) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
}

foreach ($component in $manifest.components) {
    $missing = @()
    foreach ($file in $component.files) {
        $target = [IO.Path]::GetFullPath((Join-Path $vendorRoot "$($component.id)/$($file.path)"))
        if (!$target.StartsWith($vendorRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Manifest target escapes the vendor directory.'
        }
        if (Test-Path -LiteralPath $target) {
            if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
                throw "Existing source differs from pinned manifest; will not overwrite: $target"
            }
        } else { $missing += $file }
    }
    if ($missing.Count -eq 0) { Write-Output "Verified: $($component.name)"; continue }
    $repo = ([Uri]$component.repository).AbsolutePath.Trim('/')
    $archive = Join-Path $PSScriptRoot "$($component.id).zip"
    if (!(Test-Path -LiteralPath $archive)) {
        Invoke-WebRequest "https://codeload.github.com/$repo/zip/$($component.revision)" -OutFile $archive
    }
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $verified = @()
        foreach ($file in $missing) {
            $entry = $zip.GetEntry("$($component.id)-$($component.revision)/$($file.path)")
            if ($null -eq $entry) { throw "Missing pinned archive member: $($file.path)" }
            $stream = $entry.Open()
            $buffer = [IO.MemoryStream]::new()
            try { $stream.CopyTo($buffer); $bytes = $buffer.ToArray() }
            finally { $stream.Dispose(); $buffer.Dispose() }
            if ($bytes.Length -ne $file.bytes -or (Get-BytesHash $bytes) -ne $file.sha256) {
                throw "Archive member does not match pinned manifest: $($file.path)"
            }
            $verified += @{ Target = (Join-Path $vendorRoot "$($component.id)/$($file.path)"); Bytes = $bytes }
        }
        foreach ($file in $verified) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file.Target)) | Out-Null
            # Never overwrite local edits or a concurrent download.
            $output = [IO.File]::Open($file.Target, [IO.FileMode]::CreateNew)
            try { $output.Write($file.Bytes, 0, $file.Bytes.Length) }
            finally { $output.Dispose() }
        }
    } finally { $zip.Dispose() }
    Write-Output "Restored verified source subset: $($component.name)"
}
