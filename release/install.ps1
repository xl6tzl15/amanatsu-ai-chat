[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
$gameDirectory = (Resolve-Path -LiteralPath $GameRoot).Path
if (!(Test-Path -LiteralPath (Join-Path $gameDirectory 'AmanatsuLocation.exe')) -or
    !(Test-Path -LiteralPath (Join-Path $gameDirectory 'BepInEx/core/BepInEx.Unity.IL2CPP.dll'))) {
    throw 'AmanatsuLocation with BepInEx IL2CPP is required.'
}
if (Get-Process AmanatsuLocation -ErrorAction SilentlyContinue) { throw 'Close the game before installing.' }

# Update an existing installation where it is (e.g. BepInEx/plugins/SELF); a second copy
# elsewhere under plugins would be loaded twice.
$pluginsRoot = Join-Path $gameDirectory 'BepInEx/plugins'
$existing = @(Get-ChildItem -LiteralPath $pluginsRoot -Recurse -Filter 'Amanatsu.AiChat.dll' -File -ErrorAction SilentlyContinue)
if ($existing.Count -gt 1) {
    throw ("Multiple copies of Amanatsu.AiChat.dll were found. Keep only one, then retry:`n" + ($existing.FullName -join "`n"))
}
$installDirectory = if ($existing.Count -eq 1) { $existing[0].DirectoryName } else { $pluginsRoot }

$files = @('Amanatsu.AiChat.dll','AmanatsuAiChat/bridge.py','AmanatsuAiChat/restart_bridge.py','AmanatsuAiChat/requirements.txt','AmanatsuAiChat/language.txt')
$backup = Join-Path $gameDirectory ('BepInEx/config/amanatsu.ai-chat/backups/install-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
foreach ($relative in $files) {
    $source = Join-Path $PSScriptRoot ('BepInEx/plugins/' + $relative)
    if (!(Test-Path -LiteralPath $source)) { throw "Missing package file: $relative" }
}
if ($PSCmdlet.ShouldProcess($installDirectory, 'Install Amanatsu AI Chat and back up replaced files')) {
    foreach ($relative in $files) {
        $source = Join-Path $PSScriptRoot ('BepInEx/plugins/' + $relative)
        $target = Join-Path $installDirectory $relative
        if (Test-Path -LiteralPath $target) {
            $saved = Join-Path $backup $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $saved) | Out-Null
            Copy-Item -LiteralPath $target -Destination $saved
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
        if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw "Verification failed: $relative" }
    }
    # Bundled personality presets never replace a file the user already has.
    # personalities (Japanese) and personalities-en (English) are kept apart.
    foreach ($folder in @('personalities', 'personalities-en')) {
        $presetSource = Join-Path $PSScriptRoot "BepInEx/config/amanatsu.ai-chat/$folder"
        if (!(Test-Path -LiteralPath $presetSource)) { continue }
        $presetTarget = Join-Path $gameDirectory "BepInEx/config/amanatsu.ai-chat/$folder"
        New-Item -ItemType Directory -Force -Path $presetTarget | Out-Null
        Get-ChildItem -LiteralPath $presetSource -Filter '*.json' -File | ForEach-Object {
            $target = Join-Path $presetTarget $_.Name
            if (!(Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $_.FullName -Destination $target }
        }
    }
    Write-Output "Installed to $installDirectory. Previous files, if any: $backup"
}
