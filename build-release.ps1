param([string]$Version = '0.8.1')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-z0-9]+)?$') { throw 'Invalid version' }
$projectRoot = $PSScriptRoot
& dotnet build (Join-Path $projectRoot 'AiChat.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

# Both editions share the DLL and bridge; they differ in language marker, presets and manual.
$editions = @(
    @{ Name = 'AmanatsuAiChat-' + $Version; Language = 'ja'; Presets = 'personalities'; Manual = 'README-JA.md' },
    @{ Name = 'AmanatsuAiChat-EN-' + $Version; Language = 'en'; Presets = 'personalities-en'; Manual = 'README-EN.md' }
)
foreach ($edition in $editions) {
    $releaseRoot = Join-Path $projectRoot ('dist/' + $edition.Name)
    if (Test-Path -LiteralPath $releaseRoot) { throw "Release already exists: $releaseRoot" }
}
foreach ($edition in $editions) {
    $releaseRoot = Join-Path $projectRoot ('dist/' + $edition.Name)
    $payload = Join-Path $releaseRoot 'BepInEx/plugins'
    $helpers = Join-Path $payload 'AmanatsuAiChat'
    New-Item -ItemType Directory -Path $helpers -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'bin/Release/net6.0/Amanatsu.AiChat.dll') -Destination $payload
    foreach ($name in @('bridge.py','restart_bridge.py','requirements.txt')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $helpers
    }
    [IO.File]::WriteAllText((Join-Path $helpers 'language.txt'), $edition.Language)
    foreach ($name in @('install.ps1','uninstall.ps1',$edition.Manual)) {
        Copy-Item -LiteralPath (Join-Path $projectRoot ('release/' + $name)) -Destination $releaseRoot
    }
    # Personality presets are installed only where the user has no file of the same name.
    $presets = Join-Path $releaseRoot ('BepInEx/config/amanatsu.ai-chat/' + $edition.Presets)
    New-Item -ItemType Directory -Path $presets -Force | Out-Null
    Copy-Item -Path (Join-Path $projectRoot ('release/' + $edition.Presets + '/*.json')) -Destination $presets
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README-AI.md') -Destination $releaseRoot
    $manifest = Get-ChildItem -LiteralPath $releaseRoot -File -Recurse | ForEach-Object {
        [PSCustomObject]@{ file=$_.FullName.Substring($releaseRoot.Length+1); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRoot 'manifest.json') -Encoding UTF8
    Compress-Archive -LiteralPath $releaseRoot -DestinationPath ($releaseRoot + '.zip')
    Write-Output ($releaseRoot + '.zip')
}
