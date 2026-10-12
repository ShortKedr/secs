param([string]$Study = "Benchmarks.Local/tmp/mono-study-20261012")
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$studyPath = [IO.Path]::GetFullPath((Join-Path $root $Study))
if (-not $studyPath.StartsWith((Join-Path $root "Benchmarks.Local/tmp") + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Project-local ignored study required" }
$cache = Join-Path $studyPath "D-private-cache"
if (Test-Path -LiteralPath $cache) { throw "Refusing to reuse cache" }
$packs = Join-Path $root "Benchmarks.Local/tmp/dotnet-aot/packs"
$sdk = Join-Path $packs "Microsoft.NET.Runtime.Emscripten.3.1.34.Sdk.win-x64/8.0.31/tools"
$python = Join-Path $packs "Microsoft.NET.Runtime.Emscripten.3.1.34.Python.win-x64/8.0.31/tools"
$node = Join-Path $packs "Microsoft.NET.Runtime.Emscripten.3.1.34.Node.win-x64/8.0.31/tools/bin/node.exe"
$original = Join-Path $packs "Microsoft.NET.Runtime.Emscripten.3.1.34.Cache.win-x64/8.0.31/tools/emscripten/cache"
Copy-Item -LiteralPath $original -Destination $cache -Recurse
$env:DOTNET_EMSCRIPTEN_LLVM_ROOT = Join-Path $sdk "bin"
$env:DOTNET_EMSCRIPTEN_BINARYEN_ROOT = $sdk
$env:DOTNET_EMSCRIPTEN_NODE_JS = $node
$env:EM_CONFIG = Join-Path $sdk "emscripten/.emscripten"
$env:EM_CACHE = $cache
$env:EM_FROZEN_CACHE = "0"
$env:EMSDK_PYTHON = Join-Path $python "python.exe"
$env:PYTHONPATH = $python
$env:PYTHONHOME = ""
$env:TEMP = Join-Path $studyPath "D-cache-tmp"
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Path $env:TEMP | Out-Null
$timer = [Diagnostics.Stopwatch]::StartNew()
"Command: embuilder.py build MINIMAL --lto; private cache: $cache" | Set-Content (Join-Path $studyPath "D-cache-build.log")
& $env:EMSDK_PYTHON (Join-Path $sdk "emscripten/embuilder.py") build MINIMAL --lto *>> (Join-Path $studyPath "D-cache-build.log")
$code = $LASTEXITCODE
@{ exitCode = $code; elapsedSeconds = $timer.Elapsed.TotalSeconds; cache = $cache; command = "embuilder.py build MINIMAL --lto"; sdkVersion = "3.1.34" } | ConvertTo-Json | Set-Content (Join-Path $studyPath "D-cache-build.json")
if (Test-Path (Join-Path $studyPath "D-cache-build.log")) { Get-Content (Join-Path $studyPath "D-cache-build.log") -Tail 8 }
if ($code -ne 0) { throw "LTO system cache preparation failed ($code); evidence retained" }
