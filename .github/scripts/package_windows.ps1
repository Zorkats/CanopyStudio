<#
.SYNOPSIS
    Assemble the Windows package from a published Canopy Studio.

.DESCRIPTION
    Writes CanopyStudio-<version>-windows-x64.zip to OutDir, Canopy Studio self-contained under
    one top folder, so extracting it never scatters files. Nothing is written until Canopy's
    check_windows_deps.py, from the submodule, confirms canopy_core.dll links the MSVC runtime
    statically, and the folder is checked by name for what Canopy Studio cannot start without.

.EXAMPLE
    pwsh .github/scripts/package_windows.ps1 -StudioDir build/studio-win -Version 1.2.0
#>

param(
    [Parameter(Mandatory = $true)][string] $StudioDir,
    [Parameter(Mandatory = $true)][string] $Version,
    [string] $OutDir = "dist"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# Canopy Studio cannot start without these; the rest of the folder is the .NET runtime and
# Avalonia's native libraries, which the publish step owns.
foreach ($item in @("CanopyStudio.exe", "CanopyStudio.dll", "canopy_core.dll")) {
    if (-not (Test-Path (Join-Path $StudioDir $item))) {
        Write-Error "the published Canopy Studio is missing $item"
    }
}
python (Join-Path $repoRoot "external/Canopy/.github/scripts/check_windows_deps.py") (Join-Path $StudioDir "canopy_core.dll")
if ($LASTEXITCODE -ne 0) {
    Write-Error "packaging refused: canopy_core.dll needs a DLL the package does not ship"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stageRoot = Join-Path $OutDir "stage-windows"
if (Test-Path $stageRoot) { Remove-Item -Recurse -Force $stageRoot }
$stage = Join-Path $stageRoot "CanopyStudio"
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -Recurse -Force (Join-Path $StudioDir "*") $stage
Copy-Item -Force (Join-Path $repoRoot "LICENSE") $stage
Copy-Item -Force (Join-Path $repoRoot "THIRD_PARTY_NOTICES.txt") $stage
$zip = Join-Path $OutDir "CanopyStudio-$Version-windows-x64.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $stage -DestinationPath $zip
Remove-Item -Recurse -Force $stageRoot

Write-Host ""
# Computed through .NET rather than Get-FileHash, which is missing from some constrained hosts.
$stream = [System.IO.File]::OpenRead((Resolve-Path $zip))
try {
    $digest = [System.Security.Cryptography.SHA256]::Create().ComputeHash($stream)
    "{0}  {1}" -f ([BitConverter]::ToString($digest) -replace '-', '').ToLower(), (Split-Path -Leaf $zip)
} finally {
    $stream.Dispose()
}
