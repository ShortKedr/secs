param([string]$Output = 'docs/space-battle/hardware.json')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$target = [IO.Path]::GetFullPath((Join-Path $root $Output))
if (-not $target.StartsWith($root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Hardware metadata must stay inside the current project.'
}
$cpus = @(Get-CimInstance Win32_Processor | Select-Object Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed)
$system = Get-CimInstance Win32_ComputerSystem
$os = Get-CimInstance Win32_OperatingSystem
$machineLabel = $env:SKECS_BENCHMARK_MACHINE_LABEL
$privateIdentities = @([Environment]::MachineName, [Environment]::UserName, $env:COMPUTERNAME, $env:USERNAME)
if ($machineLabel -cnotmatch '^[A-Za-z0-9_-]{1,64}$' -or
    @($privateIdentities | Where-Object { $_ -and [string]::Equals($machineLabel, $_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
    $machineLabel = 'local-machine'
}
$metadata = [ordered]@{
    Machine = $machineLabel
    CapturedAt = [DateTimeOffset]::Now.ToString('O')
    Cpu = $cpus
    TotalPhysicalMemoryBytes = [long]$system.TotalPhysicalMemory
    Os = $os.Caption
    OsVersion = $os.Version
    LogicalProcessorCountVisibleToPowerShell = [Environment]::ProcessorCount
    Notes = 'Read-only host inventory collected outside timed runs. Clock is nominal maximum, not measured frequency. Frontend JSON records the GL device and driver.'
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
[IO.File]::WriteAllText($target, ($metadata | ConvertTo-Json -Depth 6))
Write-Output $target
