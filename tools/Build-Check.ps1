<#
.SYNOPSIS
  Builds Release|x86 and Release|x64 with Visual Studio's MSBuild and checks the DLLs with dumpbin.

.DESCRIPTION
  1. Finds Visual Studio's MSBuild.exe and dumpbin.exe with vswhere (no PATH setup needed).
  2. Builds the solution for x86 and x64 (Release).
  3. For each of the two patched DLLs, checks the machine type (x86 / x64) and that all 16
     ATS exports are present. A DLL without exports loads into BVE without any error and then
     does nothing, so this check is not optional.

  Run it from the repository (or plugin) root, or from anywhere: it looks for the .sln next to tools\.
  Exit code 0 = everything is OK, 1 = a build failed or a DLL is wrong.
  This does NOT prove the plugin works in BVE; that has to be checked in BVE itself.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Build-Check.ps1

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Build-Check.ps1 -SkipBuild
  # only check the DLLs that were built before
#>
param(
    # Default: the only .sln in the folder above tools\.
    [string]$Solution,
    [switch]$SkipBuild,
    # Print the full MSBuild log even when the build succeeds (by default only a failed build prints it).
    [switch]$ShowBuildLog,
    # After a successful check, collect the files to put next to the vehicle into this folder:
    # <Name>.dll (x86), <Name>64.dll (x64), the dependency DLLs of NuGet packages (they are built into
    # bin\<Platform>\Release\net48\, not next to the patched plugin DLL) and <Name>.ini.sample.
    # Data files of your own (wav, ...) are not collected; copy them yourself.
    [string]$Stage
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $Solution) {
    $slns = @(Get-ChildItem -LiteralPath $root -Filter *.sln -File)
    if ($slns.Count -ne 1) { throw "Expected exactly one .sln in $root (found $($slns.Count)). Pass -Solution." }
    $Solution = $slns[0].FullName
}

$expected = @('Load', 'Dispose', 'GetPluginVersion', 'SetVehicleSpec', 'Initialize', 'Elapse', 'SetPower', 'SetBrake',
              'SetReverser', 'KeyDown', 'KeyUp', 'HornBlow', 'DoorOpen', 'DoorClose', 'SetSignal', 'SetBeaconData')

# --- locate Visual Studio tools -------------------------------------------------------------
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'vswhere.exe not found. Install Visual Studio 2022 (with the .NET desktop development workload).' }
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild.exe not found by vswhere.' }
$vsPath = & $vswhere -latest -property installationPath
$dumpbin = Get-ChildItem -LiteralPath (Join-Path $vsPath 'VC\Tools\MSVC') -Recurse -Filter dumpbin.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'Hostx64\\x64' } | Select-Object -First 1 -ExpandProperty FullName
if (-not $dumpbin) { throw 'dumpbin.exe not found. Install the "MSVC C++ build tools" component in Visual Studio Installer.' }
Write-Host "MSBuild : $msbuild"
Write-Host "dumpbin : $dumpbin"

# --- build ----------------------------------------------------------------------------------
if (-not $SkipBuild) {
    foreach ($platform in 'x86', 'x64') {
        Write-Host "`n=== Build Release|$platform ==="
        # Keep the output short: show the build log only when the build fails (or with -ShowBuildLog).
        # (Windows PowerShell 5.1 turns native stderr into errors under 'Stop', so relax it for this call.)
        $ErrorActionPreference = 'Continue'
        $log = & $msbuild $Solution /restore /p:Configuration=Release /p:Platform=$platform /v:minimal 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($ShowBuildLog -or $code -ne 0) { $log | ForEach-Object { Write-Host $_ } }
        if ($code -ne 0) { Write-Host "BUILD FAILED (Release|$platform)"; exit 1 }
        Write-Host "Build OK (Release|$platform)"
    }
}

# --- check the DLLs -------------------------------------------------------------------------
# The project name (the .csproj file name) is the x86 assembly name; the x64 one has "64" appended.
$proj = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -Filter *.csproj | Select-Object -First 1
if (-not $proj) { throw "No .csproj found under $root\src" }
$projDir = $proj.DirectoryName
$name = [IO.Path]::GetFileNameWithoutExtension($proj.Name)
$targets = @(
    @{ Label = 'x86'; Machine = '14C machine \(x86\)';  Path = Join-Path $projDir "bin\x86\Release\net48\x86\$name.dll" },
    @{ Label = 'x64'; Machine = '8664 machine \(x64\)'; Path = Join-Path $projDir "bin\x64\Release\net48\x64\${name}64.dll" }
)

$ok = $true
foreach ($t in $targets) {
    Write-Host "`n=== Check $($t.Label): $($t.Path) ==="
    if (-not (Test-Path -LiteralPath $t.Path)) { Write-Host 'FAIL: file not found'; $ok = $false; continue }

    $headers = (& $dumpbin /headers $t.Path) -join "`n"
    $machineOk = $headers -match $t.Machine

    $exports = @()
    foreach ($line in (& $dumpbin /exports $t.Path)) {
        if ($line -match '^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]{8}\s+(\w+)') { $exports += $Matches[1] }
    }
    $missing = @($expected | Where-Object { $exports -notcontains $_ })

    Write-Host ("machine : " + $(if ($machineOk) { 'OK' } else { 'WRONG (expected ' + $t.Label + ')' }))
    Write-Host ("exports : {0} found, {1} missing{2}" -f $exports.Count, $missing.Count, $(if ($missing.Count) { ': ' + ($missing -join ', ') } else { '' }))
    if (-not $machineOk -or $missing.Count -gt 0) { $ok = $false }
}

# --- stage the files for distribution (optional) -------------------------------------------------
if ($ok -and $Stage) {
    Write-Host "`n=== Stage -> $Stage ==="
    # Start from an empty folder so that dependency DLLs from an older build do not stay behind.
    # Only a folder that this script created (it has the marker file) is emptied; any other non-empty folder is refused,
    # so that a mistyped -Stage (the project folder, a BVE folder, ...) is never wiped.
    $marker = '.staged-by-Build-Check'
    if (Test-Path -LiteralPath $Stage) {
        $existing = @(Get-ChildItem -LiteralPath $Stage -Force)
        if ($existing.Count -gt 0) {
            if (-not (Test-Path -LiteralPath (Join-Path $Stage $marker))) {
                Write-Host "FAIL    : $Stage is not empty and was not created by -Stage. Pass a new or empty folder."
                Write-Host 'RESULT: FAIL'
                exit 1
            }
            $existing | Remove-Item -Recurse -Force
            Write-Host 'cleaned : removed the files of the previous stage'
        }
    }
    New-Item -ItemType Directory -Force -Path $Stage | Out-Null
    Set-Content -LiteralPath (Join-Path $Stage $marker) -Value 'Created by tools/Build-Check.ps1 -Stage. This folder is emptied on the next -Stage run. Not needed in the distribution.' -Encoding ascii
    $markerItem = Get-Item -LiteralPath (Join-Path $Stage $marker) -Force
    $markerItem.Attributes = $markerItem.Attributes -bor [IO.FileAttributes]::Hidden
    $x86Dir = Join-Path $projDir 'bin\x86\Release\net48'
    $x64Dir = Join-Path $projDir 'bin\x64\Release\net48'

    Copy-Item -LiteralPath $targets[0].Path -Destination $Stage -Force
    Copy-Item -LiteralPath $targets[1].Path -Destination $Stage -Force
    Write-Host "plugin  : $name.dll, ${name}64.dll"

    # Dependency DLLs: everything else in net48\ except the unpatched plugin DLLs themselves.
    $deps = @{}
    foreach ($dir in $x86Dir, $x64Dir) {
        foreach ($f in Get-ChildItem -LiteralPath $dir -Filter *.dll -File) {
            if ($f.Name -eq "$name.dll" -or $f.Name -eq "${name}64.dll") { continue }
            if ($deps.ContainsKey($f.Name)) {
                if ((Get-FileHash $deps[$f.Name]).Hash -ne (Get-FileHash $f.FullName).Hash) {
                    Write-Host "WARN    : $($f.Name) differs between the x86 and x64 builds (a platform-specific DLL?). Only the x86 one was staged; check by hand."
                }
            }
            else { $deps[$f.Name] = $f.FullName }
        }
    }
    foreach ($k in ($deps.Keys | Sort-Object)) { Copy-Item -LiteralPath $deps[$k] -Destination $Stage -Force }
    Write-Host ("deps    : {0}{1}" -f $deps.Count, $(if ($deps.Count) { ' (' + (($deps.Keys | Sort-Object) -join ', ') + ')' } else { '' }))

    $ini = Join-Path $projDir "$name.ini.sample"
    if (Test-Path -LiteralPath $ini) { Copy-Item -LiteralPath $ini -Destination $Stage -Force; Write-Host "ini     : $name.ini.sample" }
    Write-Host 'Not collected: your own data files (wav, ...) and the licence texts of the packages. Add them by hand.'
}

Write-Host ''
if ($ok) { Write-Host 'RESULT: OK (build and exports verified; NOT verified in BVE)'; exit 0 }
else { Write-Host 'RESULT: FAIL'; exit 1 }
