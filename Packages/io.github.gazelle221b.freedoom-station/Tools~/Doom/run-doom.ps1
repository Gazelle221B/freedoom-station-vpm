param(
    [ValidateSet('Prepare','Import','Verify','Probe','Memory','Isa','Errors','Board','Build')][string]$Mode='Verify',
    [int]$Timeout=1800,
    [int]$Ticks=4096,
    [Parameter(Mandatory=$true)][string]$Project
)
$ErrorActionPreference='Stop'
$taskProject=[IO.Path]::GetFullPath($Project)
if(-not (Test-Path (Join-Path $taskProject 'ProjectSettings/ProjectVersion.txt'))){throw 'Project must be a Unity project.'}
$taskSource=if($env:RVC_DOOM_SOURCE){$env:RVC_DOOM_SOURCE}else{Join-Path $PSScriptRoot 'cache/rvc-doom'}
if(-not $env:UNITY_EDITOR){throw 'Set UNITY_EDITOR to the Unity 2022.3.22f1 Editor executable.'}
if(-not $env:RVC_PERL){
    $taskPerl=Get-Command perl -ErrorAction SilentlyContinue
    if($taskPerl){$env:RVC_PERL=$taskPerl.Source}else{
        $taskPerlPath=Join-Path $env:USERPROFILE 'scoop/apps/msys2/current/usr/bin/perl.exe'
        if(Test-Path -LiteralPath $taskPerlPath){$env:RVC_PERL=$taskPerlPath}
    }
}
if(-not $env:RVC_PERLPP){$env:RVC_PERLPP=Join-Path $taskSource 'doom_payload/cache/perlpp/bin/perlpp'}
$env:PERLIO=':unix:crlf'
if(-not $env:DOOM_OUTPUT_ROOT){$env:DOOM_OUTPUT_ROOT=Join-Path $taskProject 'UserSettings/Doom'}
New-Item -ItemType Directory -Force -Path $env:DOOM_OUTPUT_ROOT | Out-Null
$taskLog=Join-Path $env:DOOM_OUTPUT_ROOT "unity-$Mode.log"
$env:DOOM_VERIFY_TIMEOUT=$Timeout.ToString()
$env:DOOM_VERIFY_TICKS=$Ticks.ToString()
$env:DOOM_VERIFY_FRAMES='3'
if(-not $env:DOOM_VERIFY_DTB){$env:DOOM_VERIFY_DTB='doom-auto-trace'}
if(-not $env:DOOM_GOLDEN){$env:DOOM_GOLDEN=Join-Path $taskSource 'doom_payload/build/golden-freedoom-auto.txt'}
if(-not $env:DOOM_VERIFY_OUT){$env:DOOM_VERIFY_OUT=Join-Path $env:DOOM_OUTPUT_ROOT 'verification'}
if(-not $env:DOOM_M_REFERENCE){$env:DOOM_M_REFERENCE=Join-Path $taskSource 'doom_payload/build/m-reference.bin'}
if(-not $env:DOOM_PROBE_OUT){$env:DOOM_PROBE_OUT=Join-Path $env:DOOM_OUTPUT_ROOT 'probe'}
if(-not $env:DOOM_ISA_DATA){$env:DOOM_ISA_DATA=Join-Path $taskSource 'doom_payload/build/isa-textures'}
if(-not $env:DOOM_ISA_OUT){$env:DOOM_ISA_OUT=Join-Path $env:DOOM_OUTPUT_ROOT 'isa'}
if(-not $env:DOOM_MEMORY_OUT){$env:DOOM_MEMORY_OUT=Join-Path $env:DOOM_OUTPUT_ROOT 'memory'}
if(-not $env:DOOM_BUILD_OUT){$env:DOOM_BUILD_OUT=Join-Path $env:DOOM_OUTPUT_ROOT 'sdk-build'}
if(-not $env:DOOM_ERRORS_OUT){$env:DOOM_ERRORS_OUT=Join-Path $env:DOOM_OUTPUT_ROOT 'errors'}
if(-not $env:DOOM_BOARD_OUT){$env:DOOM_BOARD_OUT=Join-Path $env:DOOM_OUTPUT_ROOT 'board'}
$taskMethod=@{Prepare='DoomPackage.InstallRun';Import='DoomWorldTextures.Run';Verify='DoomVerify.Run';Probe='DoomMulhProbe.Run';Memory='DoomMemoryStoreTests.Run';Isa='DoomIsaTests.Run';Errors='DoomWorldErrorTests.Run';Board='DoomLicenseBoardBuild.ValidateRun';Build='DoomBuild.Run'}[$Mode]
$taskProcess=Start-Process -FilePath $env:UNITY_EDITOR -ArgumentList @('-batchmode','-force-d3d11','-projectPath',('"'+$taskProject+'"'),'-executeMethod',$taskMethod,'-logFile',('"'+$taskLog+'"')) -WindowStyle Hidden -PassThru
Write-Output "Unity $Mode PID=$($taskProcess.Id) project=$taskProject"
if(-not $taskProcess.WaitForExit(($Timeout+240)*1000)){$taskProcess.Kill();throw "Unity $Mode timed out; see $taskLog"}
if($taskProcess.ExitCode -ne 0){throw "Unity $Mode failed ($($taskProcess.ExitCode)); see $taskLog"}
Write-Output "Unity $Mode passed; log=$taskLog"
