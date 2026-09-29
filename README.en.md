# Amanatsu AI Chat

English | [日本語](README.md)

An unofficial mod for *Amanatsu Location* (甘夏ろけーしょん). It adds a dedicated chat screen where you talk freely with a character through AI. You can use either a local model on your PC (Ollama) or an API such as OpenAI's (any OpenAI-compatible API). With every reply, the AI chooses her expression, motion, standing pose and clothing. You can create, edit and delete the expressions the AI chooses from on screen.

- Version: 0.7.2 (Japanese and English editions)
- Tested with: game 1.0.2 / BepInEx 6.0.0-be.788 (IL2CPP)
- License: MIT ([LICENSE](LICENSE))

This is an unofficial mod, not affiliated with ILLGAMES. The game, character cards, LLM models and fonts are not included.

## For players

Download the ZIP from [Releases](https://github.com/xl6tzl15/amanatsu-ai-chat/releases/latest). The English edition is `AmanatsuAiChat-EN-<version>.zip`; the Japanese edition is `AmanatsuAiChat-<version>.zip`. In the English edition the mod's screens, the AI instructions and the bundled personality presets are all in English, and the character talks in English.

For installation, controls and troubleshooting, see the user manual: [release/README-EN.md](release/README-EN.md).

## Building from source

The project references the game's assemblies (BepInEx and `interop-AmanatsuLocation`) by relative path, so place this repository at `ModSource/AiChat` inside the game folder.

```
<game folder>/
  BepInEx/                  # BepInEx 6 IL2CPP, with interop-AmanatsuLocation generated
  ModSource/AiChat/         # this repository
```

Requirements:

- .NET 6 SDK
- Python 3.10 or later (`pip install -r requirements.txt`)
- Start the game once so BepInEx generates `BepInEx/interop-AmanatsuLocation`

From the game folder, build the DLL and both release ZIPs with:

```powershell
dotnet build .\ModSource\AiChat\AiChat.csproj -c Release
./ModSource/AiChat/build-release.ps1
```

The Japanese and English ZIPs are written to `ModSource/AiChat/dist/`. How to run the tests, the bridge contract and the design rules are in [README-AI.md](README-AI.md), written for developers and AI agents.

## Layout

| Path | Contents |
| --- | --- |
| `Plugin.cs`, `Config/`, `UI/`, `GameAdapter/`, `Sequence/`, `LLM/` | The BepInEx plugin (C#) |
| `Localization.cs` | Japanese / English switching |
| `bridge.py`, `restart_bridge.py` | Local HTTP bridge between the game and the LLM |
| `release/` | User manuals (Japanese and English), installer, bundled personality presets (`personalities`, `personalities-en`) |
| `build-release.ps1` | Builds the release ZIPs |
| `Tests/`, `test_*.py` | Tests (the ones that drive the game need diagnostics mode) |
