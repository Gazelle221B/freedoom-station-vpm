param([string]$SdkSource = $env:RVC_SDK_SOURCE)
$ErrorActionPreference = 'Stop'
if (-not $SdkSource) { throw 'Set RVC_SDK_SOURCE to an existing Unity 2022.3.22f1 VPM Worlds project.' }
$taskRepo = Split-Path $PSScriptRoot -Parent
$taskProject = Join-Path $PSScriptRoot 'build/UnityProject'
if (-not (Test-Path "$taskProject/Packages/manifest.json")) {
    if (-not $env:UNITY_EDITOR) { throw 'Set UNITY_EDITOR to Unity 2022.3.22f1 Editor/Unity.exe.' }
    $taskCreateLog = Join-Path $PSScriptRoot 'build/unity-create.log'
    $taskProcess = Start-Process -FilePath $env:UNITY_EDITOR -ArgumentList @('-batchmode','-force-d3d11','-createProject',('"'+$taskProject+'"'),'-quit','-logFile',('"'+$taskCreateLog+'"')) -WindowStyle Hidden -PassThru -Wait
    if ($taskProcess.ExitCode -ne 0) { throw "Unity project creation failed. See $taskCreateLog" }
}
New-Item -ItemType Directory -Force -Path "$taskProject/Assets/_Nix", "$taskProject/Assets/Doom/Editor", "$taskProject/Assets/Doom/Runtime", "$taskProject/Packages" | Out-Null
# Keep Unity's generated manifest. Embedded SDK package.json files resolve their
# required dependencies; importing unrelated Unity 6 editor packages breaks 2022.
New-Item -ItemType Directory -Force -Path "$taskProject/ProjectSettings" | Out-Null
Copy-Item -Path "$SdkSource/ProjectSettings/*" -Destination "$taskProject/ProjectSettings" -Recurse -Force
foreach ($taskPackage in @('com.vrchat.base','com.vrchat.worlds','com.vrchat.core.vpm-resolver')) {
    New-Item -ItemType Directory -Force -Path "$taskProject/Packages/$taskPackage" | Out-Null
    Copy-Item -Path "$SdkSource/Packages/$taskPackage/*" -Destination "$taskProject/Packages/$taskPackage" -Recurse -Force
}
New-Item -ItemType Directory -Force -Path "$taskProject/Assets/_Nix/rvc" | Out-Null
Copy-Item -Path "$taskRepo/_Nix/rvc/*" -Destination "$taskProject/Assets/_Nix/rvc" -Recurse -Force
Copy-Item -Path "$PSScriptRoot/unity/Editor/*" -Destination "$taskProject/Assets/Doom/Editor" -Force
if (Test-Path "$PSScriptRoot/unity/Runtime") { Copy-Item -Path "$PSScriptRoot/unity/Runtime/*" -Destination "$taskProject/Assets/Doom/Runtime" -Force }
if (Test-Path "$taskRepo/_Nix/AutoImport.cs") { Copy-Item -LiteralPath "$taskRepo/_Nix/AutoImport.cs" -Destination "$taskProject/Assets/Doom/Editor/AutoImport.cs" -Force }
if (Test-Path "$taskRepo/_Nix/AutoImport.cs.meta") { Copy-Item -LiteralPath "$taskRepo/_Nix/AutoImport.cs.meta" -Destination "$taskProject/Assets/Doom/Editor/AutoImport.cs.meta" -Force }
New-Item -ItemType Directory -Force -Path "$taskProject/Assets/Doom/Generated" | Out-Null
Copy-Item -Path "$PSScriptRoot/build/unity-textures/*.png" -Destination "$taskProject/Assets/Doom/Generated" -Force
Write-Output "UNITY_PROJECT=$taskProject"
