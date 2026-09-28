[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
$gameDirectory = (Resolve-Path -LiteralPath $GameRoot).Path
if (!(Test-Path -LiteralPath (Join-Path $gameDirectory 'AmanatsuLocation.exe'))) { throw 'Invalid game directory' }
if (Get-Process AmanatsuLocation -ErrorAction SilentlyContinue) { throw 'Close the game first.' }
$pluginsRoot = Join-Path $gameDirectory 'BepInEx/plugins'
$installs = @(Get-ChildItem -LiteralPath $pluginsRoot -Recurse -Filter 'Amanatsu.AiChat.dll' -File -ErrorAction SilentlyContinue | ForEach-Object DirectoryName)
if ($installs.Count -eq 0) { Write-Output 'Amanatsu AI Chat is not installed.'; return }
$backup = Join-Path $gameDirectory ('BepInEx/config/amanatsu.ai-chat/backups/uninstall-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$files = @('Amanatsu.AiChat.dll','AmanatsuAiChat/bridge.py','AmanatsuAiChat/restart_bridge.py','AmanatsuAiChat/requirements.txt','AmanatsuAiChat/language.txt')
if ($PSCmdlet.ShouldProcess(($installs -join ', '), 'Move only AI Chat package files to a recoverable backup')) {
    $index = 0
    foreach ($directory in $installs) {
        foreach ($relative in $files) {
            $target = Join-Path $directory $relative
            if (Test-Path -LiteralPath $target) {
                $saved = Join-Path $backup ("$index/" + $relative)
                New-Item -ItemType Directory -Force -Path (Split-Path $saved) | Out-Null
                Move-Item -LiteralPath $target -Destination $saved
            }
        }
        $index++
    }
    Write-Output "Mod files moved to $backup. Settings, logs, game data and cards are retained."
}
