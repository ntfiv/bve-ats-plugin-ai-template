<#
.SYNOPSIS
  Loads the staged plugin DLLs the way BVE does (LoadLibrary + GetProcAddress) and calls a few exports,
  to find "the dependency DLL was not found" problems without starting BVE.

.DESCRIPTION
  Build-Check.ps1 only proves that the exports exist. A plugin that uses an external NuGet package can still
  fail at run time inside BVE (FileNotFoundException for the package DLL) and then silently does nothing.
  This script puts the plugin into the same situation:
    - PowerShell's own folder plays the role of BVE's folder (it is NOT the plugin folder), so the .NET runtime
      cannot find dependency DLLs by itself; only the plugin's AssemblyResolve (AtsExports.cs) can.
    - Calls: Load, GetPluginVersion, SetVehicleSpec, Initialize, KeyDown/KeyUp for the keys you list,
      Elapse (with empty panel/sound buffers), Dispose.
    - Prints which assemblies were loaded from the plugin folder, and the plugin's log lines written during the run
      (the log is %TEMP%\<Name>.log; it is only written when the ini does not switch the log off).
  x64 is tested in a 64-bit PowerShell and x86 in a 32-bit PowerShell (both ship with Windows).

  Typical use, after Build-Check.ps1 -Stage:
    powershell -ExecutionPolicy Bypass -File tools\Smoke-Test.ps1 -Dir <stage folder> -KeyDown 0 -Require NAudio.Core

  This is NOT a test in BVE. It does not prove the plugin works there (BVE's own loading may differ).
  Exit code 0 = all calls ran and every -Require assembly was loaded; 1 = a failure.

.PARAMETER Dir
  Folder with <Name>.dll, <Name>64.dll and the dependency DLLs (what Build-Check.ps1 -Stage produced).
.PARAMETER Name
  Plugin name. Default: the base name of the *.ini.sample in -Dir, or the only <Name>.dll / <Name>64.dll pair.
.PARAMETER KeyDown
  ATS key indices (0 = S, 1 = A1, ...) to send with KeyDown, then KeyUp.
.PARAMETER Require
  Assembly simple names that must be loaded from -Dir during the run (e.g. NAudio.Core). Fails if one is not.
.PARAMETER PanelCapacity
  Number of elements in the panel buffer given to Elapse. Default 256 (indices 0-255, the normal BVE range).
  Use 1024 for a plugin that declares indices up to 1023 for an AtsArrayExtender environment (docs/KNOWHOW.md 1-2).
.PARAMETER SoundCapacity
  Same for the sound buffer.

  After Load, the declared ranges (AtsLogic.PanelIndexMin/Max, PanelReadIndexMin/Max, SoundIndexMin/Max,
  SoundReadIndexMin/Max) are read from the loaded plugin by reflection. A non-empty range that is negative or reaches
  past the capacity fails the test, and Elapse is then not called (it could touch memory outside the buffer).
  If AtsLogic or any of these constants cannot be found, the ranges cannot be confirmed: the test also fails and
  Elapse is not called (fail-closed). An empty range (Min > Max) counts as checked.
#>
param(
    [Parameter(Mandatory = $true)][string]$Dir,
    [string]$Name,
    [string[]]$KeyDown = @(),
    [string[]]$Require = @(),
    # Number of int elements in the panel / sound buffers passed to Elapse (BVE normally has 256: indices 0-255).
    [ValidateRange(1, 65536)][int]$PanelCapacity = 256,
    [ValidateRange(1, 65536)][int]$SoundCapacity = 256,
    # internal: run the test in this process (used by the outer call with the matching PowerShell bitness)
    [ValidateSet('', 'x86', 'x64')][string]$Inner = ''
)

$ErrorActionPreference = 'Stop'
$Dir = (Resolve-Path -LiteralPath $Dir).Path

# "-KeyDown 0,1" and "-Require A,B" may arrive as one comma-separated string when passed through -File; split them.
[int[]]$KeyDown = @(($KeyDown -join ',') -split ',' | Where-Object { $_.Trim() } | ForEach-Object { [int]$_.Trim() })
[string[]]$Require = @(($Require -join ',') -split ',' | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() })

if (-not $Name) {
    $ini = @(Get-ChildItem -LiteralPath $Dir -Filter *.ini.sample -File)
    if ($ini.Count -eq 1) { $Name = $ini[0].Name -replace '\.ini\.sample$', '' }
    else { throw "Cannot decide the plugin name. Pass -Name." }
}

# ============================== inner: runs inside PowerShell of the right bitness ==============================
if ($Inner) {
    $dllName = if ($Inner -eq 'x64') { "${Name}64.dll" } else { "$Name.dll" }
    $dllPath = Join-Path $Dir $dllName
    if (-not (Test-Path -LiteralPath $dllPath)) { Write-Host "FAIL: $dllPath not found"; exit 1 }

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SmokeNative {
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] public static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)] public static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32")] public static extern bool FreeLibrary(IntPtr module);

    [StructLayout(LayoutKind.Sequential)] public struct Spec { public int Brake, Power, Ats, B67, Cars; }
    [StructLayout(LayoutKind.Sequential)] public struct State { public double Location; public float Speed; public int Time; public float Bc, Mr, Er, Bp, Sap, Current; }
    [StructLayout(LayoutKind.Sequential)] public struct Handles { public int Brake, Power, Reverser, ConstantSpeed; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void FnVoid();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int FnInt();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void FnIntArg(int a);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void FnSpec(Spec s);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate Handles FnElapse(State s, IntPtr panel, IntPtr sound);
}
'@

    $before = @([AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object { $_.GetName().Name })
    $logPath = Join-Path ([IO.Path]::GetTempPath()) ($(if ($Inner -eq 'x64') { "${Name}64.log" } else { "$Name.log" }))
    $logBefore = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).Length } else { 0 }

    $fail = $false
    $module = [SmokeNative]::LoadLibrary($dllPath)
    if ($module -eq [IntPtr]::Zero) {
        Write-Host ("FAIL: LoadLibrary failed (Win32 error {0}). A 32/64-bit mismatch or a missing native dependency." -f [Runtime.InteropServices.Marshal]::GetLastWin32Error())
        exit 1
    }
    function Get-Fn([string]$export, [Type]$type) {
        $p = [SmokeNative]::GetProcAddress($module, $export)
        if ($p -eq [IntPtr]::Zero) { throw "export '$export' not found" }
        return [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($p, $type)
    }
    function Step([string]$label, [scriptblock]$body) {
        try { & $body; Write-Host ("  ok   : {0}" -f $label) }
        catch { Write-Host ("  FAIL : {0} -> {1}" -f $label, $_.Exception.Message); $script:fail = $true }
    }

    # Order of the classic ATS plugin API: Load -> GetPluginVersion -> SetVehicleSpec -> Initialize.
    Step 'Load' { (Get-Fn 'Load' ([SmokeNative+FnVoid])).Invoke() }
    Step 'GetPluginVersion' { $v = (Get-Fn 'GetPluginVersion' ([SmokeNative+FnInt])).Invoke(); Write-Host ("           version 0x{0:X8}" -f $v) }

    # Declared Panel/Sound ranges vs. the buffer capacity (the managed assembly is loaded by now).
    # Fail-closed: Elapse is called only when all four ranges were read and fit in the buffers.
    $rangeOk = $false
    $asm = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {
        -not $_.IsDynamic -and $_.Location -and $_.Location.Equals($dllPath, [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
    $logicType = $null
    if ($asm) {
        $logicType = $asm.GetType("$Name.AtsLogic", $false)
        if (-not $logicType) {
            try { $logicType = $asm.GetTypes() | Where-Object { $_.Name -eq 'AtsLogic' } | Select-Object -First 1 } catch { }
        }
    }
    if (-not $logicType) {
        Write-Host '  FAIL : could not find AtsLogic in the loaded plugin, so the declared ranges cannot be checked against the buffers.'
        $script:fail = $true
    }
    else {
        $checked = 0; $bad = 0
        function Get-Const([string]$field) {
            $f = $logicType.GetField($field, [Reflection.BindingFlags]'Public, NonPublic, Static')
            if ($f -and $f.IsLiteral) { return [int]$f.GetRawConstantValue() }
            if ($f) { return [int]$f.GetValue($null) }
            return $null
        }
        foreach ($r in @(
                @{ Label = 'panel write'; Min = 'PanelIndexMin'; Max = 'PanelIndexMax'; Cap = $PanelCapacity; Opt = 'PanelCapacity' },
                @{ Label = 'panel read '; Min = 'PanelReadIndexMin'; Max = 'PanelReadIndexMax'; Cap = $PanelCapacity; Opt = 'PanelCapacity' },
                @{ Label = 'sound write'; Min = 'SoundIndexMin'; Max = 'SoundIndexMax'; Cap = $SoundCapacity; Opt = 'SoundCapacity' },
                @{ Label = 'sound read '; Min = 'SoundReadIndexMin'; Max = 'SoundReadIndexMax'; Cap = $SoundCapacity; Opt = 'SoundCapacity' })) {
            $min = Get-Const $r.Min; $max = Get-Const $r.Max
            if ($null -eq $min -or $null -eq $max) {
                Write-Host ("  FAIL : {0}: AtsLogic.{1} / {2} not found, so this range cannot be checked." -f $r.Label, $r.Min, $r.Max)
                $bad++; $script:fail = $true; continue
            }
            $checked++
            if ($min -gt $max) { Write-Host ("  ok   : {0} range: empty ({1}..{2})" -f $r.Label, $min, $max); continue }
            if ($min -lt 0 -or $max -ge $r.Cap) {
                Write-Host ("  FAIL : {0} range {1}..{2} is outside the buffer 0..{3} (-{4} {5})." -f $r.Label, $min, $max, ($r.Cap - 1), $r.Opt, $r.Cap)
                $bad++; $script:fail = $true
            }
            else { Write-Host ("  ok   : {0} range: {1}..{2} (buffer 0..{3})" -f $r.Label, $min, $max, ($r.Cap - 1)) }
        }
        $rangeOk = ($checked -eq 4 -and $bad -eq 0)
    }
    Step 'SetVehicleSpec' {
        $s = New-Object SmokeNative+Spec; $s.Brake = 8; $s.Power = 5; $s.Ats = 2; $s.B67 = 0; $s.Cars = 4
        (Get-Fn 'SetVehicleSpec' ([SmokeNative+FnSpec])).Invoke($s)
    }
    Step 'Initialize' { (Get-Fn 'Initialize' ([SmokeNative+FnIntArg])).Invoke(0) }
    foreach ($k in $KeyDown) {
        Step "KeyDown($k)" { (Get-Fn 'KeyDown' ([SmokeNative+FnIntArg])).Invoke($k) }
        Start-Sleep -Milliseconds 300   # give a plugin that starts work on another thread a moment
        Step "KeyUp($k)" { (Get-Fn 'KeyUp' ([SmokeNative+FnIntArg])).Invoke($k) }
    }
    if (-not $rangeOk) { Write-Host '  skip : Elapse (the declared ranges were not confirmed to fit in the buffers)' }
    else { Step 'Elapse' {
        $panel = [Runtime.InteropServices.Marshal]::AllocHGlobal($PanelCapacity * 4)
        $sound = [Runtime.InteropServices.Marshal]::AllocHGlobal($SoundCapacity * 4)
        try {
            for ($i = 0; $i -lt $PanelCapacity; $i++) { [Runtime.InteropServices.Marshal]::WriteInt32($panel, $i * 4, 0) }
            for ($i = 0; $i -lt $SoundCapacity; $i++) { [Runtime.InteropServices.Marshal]::WriteInt32($sound, $i * 4, 0) }
            $st = New-Object SmokeNative+State; $st.Speed = 10; $st.Time = 1000
            $h = (Get-Fn 'Elapse' ([SmokeNative+FnElapse])).Invoke($st, $panel, $sound)
            Write-Host ("           handles: brake={0} power={1} reverser={2}" -f $h.Brake, $h.Power, $h.Reverser)
        }
        finally { [Runtime.InteropServices.Marshal]::FreeHGlobal($panel); [Runtime.InteropServices.Marshal]::FreeHGlobal($sound) }
    } }
    Step 'Dispose' { (Get-Fn 'Dispose' ([SmokeNative+FnVoid])).Invoke() }

    # Which assemblies came from the plugin folder?
    $dirPrefix = $Dir.TrimEnd('\') + '\'
    $loaded = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {
            -not $_.IsDynamic -and $_.Location -and $_.Location.StartsWith($dirPrefix, [StringComparison]::OrdinalIgnoreCase)
        } | ForEach-Object { $_.GetName().Name })
    Write-Host ("  loaded from the plugin folder: " + $(if ($loaded.Count) { $loaded -join ', ' } else { '(none)' }))
    foreach ($r in $Require) {
        if ($loaded -contains $r) { Write-Host "  ok   : required assembly loaded: $r" }
        else { Write-Host "  FAIL : required assembly NOT loaded from the plugin folder: $r"; $fail = $true }
    }

    # Log lines written during this run (only if the plugin's log is on).
    if (Test-Path -LiteralPath $logPath) {
        $len = (Get-Item -LiteralPath $logPath).Length
        if ($len -gt $logBefore) {
            Write-Host "  log (new lines in $logPath):"
            $fs = [IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite'); $fs.Seek($logBefore, 'Begin') | Out-Null
            $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
            $sr.ReadToEnd().TrimEnd().Split("`n") | Select-Object -First 20 | ForEach-Object { Write-Host ("    " + $_.TrimEnd()) }
            $sr.Dispose()
        }
    }
    if ($fail) { exit 1 } else { exit 0 }
}

# ============================== outer: start one PowerShell per bitness ==============================
$win = $env:windir
if ([Environment]::Is64BitProcess) { $ps64 = "$win\System32\WindowsPowerShell\v1.0\powershell.exe"; $ps32 = "$win\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" }
else { $ps64 = "$win\Sysnative\WindowsPowerShell\v1.0\powershell.exe"; $ps32 = "$win\System32\WindowsPowerShell\v1.0\powershell.exe" }

$allOk = $true
foreach ($t in @(@{ Bits = 'x86'; Exe = $ps32 }, @{ Bits = 'x64'; Exe = $ps64 })) {
    Write-Host "`n=== Smoke test $($t.Bits) ($Name) in $Dir ==="
    if (-not (Test-Path -LiteralPath $t.Exe)) { Write-Host "SKIP: $($t.Exe) not found"; $allOk = $false; continue }
    $psArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Dir', $Dir, '-Name', $Name, '-Inner', $t.Bits,
        '-PanelCapacity', $PanelCapacity, '-SoundCapacity', $SoundCapacity)
    if ($KeyDown.Count) { $psArgs += @('-KeyDown', ($KeyDown -join ',')) }
    if ($Require.Count) { $psArgs += @('-Require', ($Require -join ',')) }
    & $t.Exe @psArgs
    if ($LASTEXITCODE -ne 0) { $allOk = $false }
}

Write-Host ''
if ($allOk) { Write-Host 'RESULT: OK (loaded and called like BVE would; NOT verified in BVE)'; exit 0 }
else { Write-Host 'RESULT: FAIL'; exit 1 }
