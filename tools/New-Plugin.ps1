<#
.SYNOPSIS
  Creates a new BVE ATS plugin project from this template.

.DESCRIPTION
  Copies the template to a new folder, renames the solution / project / namespace / assembly,
  gives the copy its own DllExportIdent and project GUID, and writes a stub README.md.
  Everything else (AtsLogic.cs, docs/, AGENTS.md, ...) is copied as-is.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\New-Plugin.ps1 -Name MyAts
  # -> creates ..\MyAts next to this repository

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\New-Plugin.ps1 -Name MyAts -Destination D:\work\MyAts
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Name,

    # Default: a folder named $Name next to the template repository.
    [string]$Destination
)

$ErrorActionPreference = 'Stop'

# The name becomes a C# namespace and a DLL file name.
if ($Name -notmatch '^[A-Za-z][A-Za-z0-9_]*$') {
    throw "Name '$Name' is not valid. Use letters, digits and '_' only, starting with a letter (it becomes a C# namespace and a DLL name)."
}
if ($Name -eq 'BveAtsPlugin') {
    throw "Choose a name other than 'BveAtsPlugin' (that is the template's own name)."
}
# The x64 build is named <Name>64, and the ini / log file names are derived by stripping a trailing "64" from
# the assembly name. A name that already ends in 64 would make the x86 and x64 builds collide.
if ($Name -match '64$') {
    throw "Name '$Name' must not end with '64' (the x64 DLL is named <Name>64, and the ini name is derived by removing a trailing 64)."
}

$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
if (-not $Destination) { $Destination = Join-Path (Split-Path -Parent $root) $Name }
# Resolve relative paths against PowerShell's current location (not the .NET current directory), and normalize "..".
$Destination = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)).TrimEnd('\')
if (Test-Path -LiteralPath $Destination) {
    throw "Destination already exists: $Destination"
}
# A destination inside the template would be picked up by the copy below and copied into itself (endless recursion).
if ($Destination.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
    $Destination.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Destination must be outside the template folder ($root): $Destination"
}

$oldName = 'BveAtsPlugin'
$oldIdent = 'C001163F-E820-47FF-BBB8-99EB529164A3'
$oldProjectGuid = 'CC2F1BEB-C521-4B96-A811-8BE02FBB6E30'
$newIdent = [guid]::NewGuid().ToString().ToUpper()
$newProjectGuid = [guid]::NewGuid().ToString().ToUpper()

# Copy only what a plugin project needs (no build output, no packages, no git data, no tools).
$excludeDirs = @('.git', 'bin', 'obj', 'packages', '.vs', 'tools', '.claude')
New-Item -ItemType Directory -Path $Destination | Out-Null

function Copy-Tree([string]$from, [string]$to) {
    foreach ($item in Get-ChildItem -LiteralPath $from -Force) {
        if ($item.PSIsContainer) {
            if ($excludeDirs -contains $item.Name) { continue }
            $sub = Join-Path $to $item.Name
            New-Item -ItemType Directory -Path $sub | Out-Null
            Copy-Tree $item.FullName $sub
        }
        else {
            Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $to $item.Name)
        }
    }
}
Copy-Tree $root $Destination

# The template README is about the template; the new plugin gets a stub instead (written below).
Remove-Item -LiteralPath (Join-Path $Destination 'README.md') -Force

# The plugin keeps only the build / smoke-test scripts from tools\ (New-Plugin.ps1 stays in the template).
$toolsDest = Join-Path $Destination 'tools'
New-Item -ItemType Directory -Path $toolsDest | Out-Null
foreach ($script in 'Build-Check.ps1', 'Smoke-Test.ps1') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination (Join-Path $toolsDest $script)
}

# Rename files and folders (deepest first).
Get-ChildItem -LiteralPath $Destination -Recurse -Force |
    Where-Object { $_.Name -like "*$oldName*" } |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object { Rename-Item -LiteralPath $_.FullName -NewName ($_.Name -replace $oldName, $Name) }

# Rewrite text files, keeping each file's BOM / no-BOM state.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$textExt = @('.cs', '.csproj', '.sln', '.sample', '.md')
Get-ChildItem -LiteralPath $Destination -Recurse -File |
    Where-Object { $textExt -contains $_.Extension } |
    ForEach-Object {
        $bytes = [IO.File]::ReadAllBytes($_.FullName)
        $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
        $text = [IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8)
        $new = $text.Replace($oldIdent, $newIdent).Replace($oldProjectGuid, $newProjectGuid)
        # Only the template's own identifiers. Docs keep their wording; code, project files and samples are renamed.
        if ($_.Extension -ne '.md') { $new = $new.Replace($oldName, $Name) }
        if ($new -ne $text) {
            $enc = if ($hasBom) { $utf8Bom } else { $utf8NoBom }
            [IO.File]::WriteAllText($_.FullName, $new, $enc)
        }
    }

# Stub README for the new plugin.
$stub = Join-Path $PSScriptRoot 'README.stub.md'
if (Test-Path -LiteralPath $stub) {
    $stubText = [IO.File]::ReadAllText($stub, [Text.Encoding]::UTF8).Replace('{{NAME}}', $Name)
    [IO.File]::WriteAllText((Join-Path $Destination 'README.md'), $stubText, $utf8NoBom)
}

# Tell an AI that opens the new folder on its own that this is a finished-from-template plugin, not the template:
# insert tools\AGENTS.banner.md right after the first line (the title) of the copied AGENTS.md.
$banner = Join-Path $PSScriptRoot 'AGENTS.banner.md'
$agents = Join-Path $Destination 'AGENTS.md'
if ((Test-Path -LiteralPath $banner) -and (Test-Path -LiteralPath $agents)) {
    $bannerText = [IO.File]::ReadAllText($banner, [Text.Encoding]::UTF8).Replace('{{NAME}}', $Name).TrimEnd()
    $agentsText = [IO.File]::ReadAllText($agents, [Text.Encoding]::UTF8)
    $nl = $agentsText.IndexOf("`n")
    if ($nl -ge 0) {
        $agentsText = $agentsText.Substring(0, $nl + 1) + "`n" + $bannerText + "`n" + $agentsText.Substring($nl + 1)
        [IO.File]::WriteAllText($agents, $agentsText, $utf8NoBom)
    }
}

Write-Host "Created: $Destination"
Write-Host "  namespace / assembly : $Name / $Name (x86), ${Name}64 (x64)"
Write-Host "  solution             : $Name.sln"
Write-Host "Next: edit src\$Name\AtsLogic.cs, set the Panel/Sound index ranges, then build Release|x86 and Release|x64 in Visual Studio 2022."
