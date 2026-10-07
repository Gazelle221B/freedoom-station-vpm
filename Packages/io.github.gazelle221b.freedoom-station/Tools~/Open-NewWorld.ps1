param(
    [string]$ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [string]$EditorPath
)

$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$versionFile = Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt'
$versionMatch = [regex]::Match([IO.File]::ReadAllText($versionFile), '(?m)^m_EditorVersion: (\S+)')
if (!$versionMatch.Success) { throw 'ProjectVersion.txt does not specify an editor version.' }
if (!$EditorPath) {
    $EditorPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$($versionMatch.Groups[1].Value)/Editor/Unity.exe"
}
if (!(Test-Path -LiteralPath $EditorPath)) { throw "Required Unity editor is not installed: $EditorPath" }

& (Join-Path $PSScriptRoot 'Repair-ClientSimPersistence.ps1') -ProjectPath $ProjectPath
$alreadyOpen = Get-CimInstance Win32_Process -Filter "name = 'Unity.exe'" | Where-Object {
    $_.CommandLine -and $_.CommandLine.Replace('/', '\').IndexOf($ProjectPath, [StringComparison]::OrdinalIgnoreCase) -ge 0
}
if ($alreadyOpen) {
    Write-Output "Project is already open in Unity (PID $($alreadyOpen.ProcessId -join ', ')). Open the new scene with Tools > New World > Prepare."
    return
}
Start-Process -FilePath $EditorPath -ArgumentList "-projectPath `"$ProjectPath`" -executeMethod NewWorldPreparation.Prepare"
