param([ValidateSet('Setup','Verify','Build')][string]$Mode='Verify', [int]$Timeout=1800, [int]$Ticks=4096)
$ErrorActionPreference='Stop'
if (-not $env:UNITY_EDITOR) { throw 'Set UNITY_EDITOR to the Unity 2022.3.22f1 executable.' }
$taskProject=Join-Path $PSScriptRoot 'build/UnityProject'
if (-not (Test-Path "$taskProject/Assets")) { throw 'Run prepare_unity_project.ps1 first.' }
if (-not $env:DOOM_VERIFY_OUT) { $env:DOOM_VERIFY_OUT=Join-Path $PSScriptRoot 'build/unity-verification' }
if (-not $env:DOOM_GOLDEN) { $env:DOOM_GOLDEN=Join-Path $PSScriptRoot 'build/golden-auto.txt' }
if (-not $env:DOOM_BUILD_OUT) { $env:DOOM_BUILD_OUT=Join-Path $PSScriptRoot 'build/sdk-build' }
$env:DOOM_VERIFY_TIMEOUT=$Timeout.ToString()
$env:DOOM_VERIFY_TICKS=$Ticks.ToString()
$taskMethod=@{ Setup='DoomBootstrap.Setup'; Verify='DoomVerify.Run'; Build='DoomBuild.Run' }[$Mode]
$taskLog=Join-Path $PSScriptRoot "build/unity-$Mode.log"
$taskProcess=Start-Process -FilePath $env:UNITY_EDITOR -ArgumentList @('-batchmode','-force-d3d11','-projectPath',('"'+$taskProject+'"'),'-executeMethod',$taskMethod,'-logFile',('"'+$taskLog+'"')) -WindowStyle Hidden -PassThru
if (-not $taskProcess.WaitForExit(($Timeout+240)*1000)) {
    $taskProcess.Kill()
    throw "Unity $Mode exceeded host timeout. See $taskLog"
}
if ($taskProcess.ExitCode -ne 0) { throw "Unity $Mode failed (exit $($taskProcess.ExitCode)). See $taskLog" }
Write-Output "Unity $Mode passed. Log: $taskLog"
