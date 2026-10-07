param([string]$ProjectPath = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $ProjectPath 'Packages/com.vrchat.worlds/Integrations/ClientSim/Runtime/Player/PlayerPersistence/ClientSimPlayerObjectStorage.cs'
$source = [IO.File]::ReadAllText($sourcePath)
if ($source.Contains('private static readonly SemaphoreSlim FileWriteLock')) {
    Write-Output 'ClientSim persistence patch is already installed.'
    return
}

$oldMethod = @'
        private async UniTask SaveToFile(string data)
        {
            await UniTask.SwitchToTaskPool();
            try{
                await File.WriteAllTextAsync(PlayerDataFilePath(_player), data);
            }
            catch (Exception e)
            {
                this.LogError($"Error saving PlayerObjects: {e.Message}");
            }
        }
'@
$newMethod = @'
        private async UniTask SaveToFile(string data)
        {
            string path = PlayerDataFilePath(_player);
            await UniTask.SwitchToTaskPool();
            await FileWriteLock.WaitAsync();
            try{
                await File.WriteAllTextAsync(path, data);
            }
            catch (Exception e)
            {
                this.LogError($"Error saving PlayerObjects: {e.Message}");
            }
            finally
            {
                FileWriteLock.Release();
            }
        }
'@
$source = $source.Replace("`r`n", "`n")
$oldMethod = $oldMethod.Replace("`r`n", "`n")
$newMethod = $newMethod.Replace("`r`n", "`n")
if (!$source.Contains($oldMethod) -or !$source.Contains('        private bool hadUpdate = false;')) {
    throw 'ClientSim SDK source changed; review the persistence implementation before applying this patch.'
}
$source = $source.Replace('using System.IO;', "using System.IO;`nusing System.Threading;")
$source = $source.Replace('        private bool hadUpdate = false;', "        private bool hadUpdate = false;`n        private static readonly SemaphoreSlim FileWriteLock = new SemaphoreSlim(1, 1);")
$source = $source.Replace($oldMethod, $newMethod)
[IO.File]::WriteAllText($sourcePath, $source, (New-Object System.Text.UTF8Encoding($false)))
Write-Output 'Installed ClientSim persistence patch.'
