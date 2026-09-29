# Amanatsu AI Chat — AI / developer README

Version: 0.7.3. This file is for coding agents, maintainers, and automated reviewers. The Japanese end-user manual is `release/README-JA.md` in the source tree and `README-JA.md` in the distribution ZIP. Do not present this file as the user installation guide.

## Scope and invariants

- BepInEx 6 IL2CPP plugin for AmanatsuLocation. The normal title screen must remain usable. Dedicated conversation starts only from the title button or configured F10 shortcut; `[UI] OpenOnStartup` defaults to `false`.
- Native ADV speaker/dialogue window shows the heroine's replies. The Mod panel handles input and controls. The native heroine frame is pink only in dedicated conversation; it is restored on exit.
- A character card is selected with a Windows file dialog. Never silently choose, read, load, save, or modify a user-edited card during maintenance or tests. Built-in-preset diagnostics are for isolated runtime checks only.
- Do not terminate the running game or overwrite a loaded plugin DLL without the user's explicit authorization for that turn. A normal build does not require closing the game; installation does.
- No game art, character card, model, API key, or font is included in the package. Game backgrounds are referenced from the installed game's `DefaultData/common/bg`, not copied.
- Treat reported checks separately: build success, package integrity, install verification, runtime readback, visual screenshot, native file-dialog operation, and real LLM inference are different claims.

## Repository map

| Path | Responsibility |
| --- | --- |
| `Localization.cs` | `L.T(ja, en)` and `L.En`. `[UI] Language=auto` follows `AmanatsuAiChat/language.txt` beside the DLL (the English package ships `en`); `ja`/`en` force a language. |
| `Plugin.cs` | BepInEx entry, title access, UI callbacks, backend settings, diagnostic commands. |
| `Config/ModSettings.cs` | Mod config bindings and default keyboard/mouse settings. |
| `Config/BackendSettings.cs` | Bridge JSON mirror and URL validation. |
| `UI/DedicatedChatUi.cs` | User panel, settings, personality and global expression editors, controls. |
| `Config/GlobalExpressionPresets.cs` | Versioned shared expression preset file; never writes card PNGs or per-card sidecars. |
| `UI/NativeFilePicker.cs` | Windows card/personality/background file dialogs. |
| `GameAdapter/DedicatedCharacterStage.cs` | Native ADV stage, camera, character lifetime, restoration. |
| `GameAdapter/BackgroundBackdrop.cs` | Runtime background image selection and rendering. |
| `GameAdapter/GraphicPreferences.cs` | Temporary graphics override and native-option synchronization. |
| `GameAdapter/MotionCatalog.cs`, `MOTION_INVENTORY.md` | Verified safe motion mapping, idle pose list (`Poses`), and wider asset inventory. |
| `GameAdapter/CharacterAdapter.cs` | Expression/openness, outfit state capture/apply, garment names, available outfit actions, pose. |
| `GameAdapter/ConversationPostProcessing.cs` | Stage-only Beautify/URP volume rebuilt from the map profile. |
| `LLM/LlmClient.cs`, `LLM/ModelCatalog.cs` | Turn requests to the bridge; model list from Ollama `/api/tags` or OpenAI `/v1/models`. |
| `Sequence/OutfitPresets.cs`, `Sequence/OutfitIntent.cs` | Named outfit silhouettes (removal-only except `dressed`); part keys and action validation. |
| `bridge.py` | Local HTTP bridge, schema-constrained upstream request, validation/sanitization. |
| `restart_bridge.py` | Restricted local bridge restart and health verification. |
| `release/README-JA.md` | Human-facing manual; keep this synchronized with released behavior. |
| `build-release.ps1`, `release/install.ps1`, `release/uninstall.ps1` | Packaging, installation with backups, recoverable removal. |

## Editions

One DLL and bridge serve two packages built by `build-release.ps1`: `AmanatsuAiChat-<v>` (Japanese, `language.txt`=ja, `release/personalities`, `README-JA.md`) and `AmanatsuAiChat-EN-<v>` (English, `language.txt`=en, `release/personalities-en`, `README-EN.md`). Every user-visible C# string goes through `L.T`. The client sends `language` to the bridge, which switches the language-specific protocol rules (dialogue language, examples, clothing vocabulary) while keeping the Japanese prompt byte-identical to earlier versions. Language-dependent data is stored apart so the two never mix: `personalities` / `personalities-en`, `expression-presets.json` / `expression-presets-en.json`, `default.json` / `default-en.json`, `characters` / `characters-en`. Expression, motion and pose descriptions have English defaults used when those files are first created.

The distributed ZIP contains a copy of this document as `README-AI.md` and the user manual as `README-JA.md`. The source tree is authoritative for implementation; the package is a snapshot.

## Runtime flow

1. `Plugin.Load()` binds BepInEx config, creates the behaviour, and leaves `OpenOnStartup=false` by default. The title and panel show `Amanatsu AI Chat v0.7.3`.
2. The title button or F10 starts dedicated mode. When no card path is configured, a native PNG picker opens; cancel leaves the title unchanged.
3. `DedicatedCharacterStage` loads a native ADV core and creates a displayed character. The normal title canvas is hidden only while dedicated mode is active. The UI and native dialogue window are distinct layers.
4. On stage open and on card change, the LLM generates the opening line from a hidden instruction (not stored in history; a player message cancels it). An input turn goes to `LlmClient` → loopback `bridge.py` → Ollama or OpenAI-compatible upstream, with the current outfit (per-part state and garment name), available outfit actions, expressions, motions and poses. The bridge validates the returned sequence; native text and allowed expression/motion/pose/outfit actions are then applied.
5. Conversation history is kept in memory for the game session only (dropped on card change or game exit; trimmed to fit the context). Exiting cancels work, restores temporary outfit/graphics/title state, disposes the stage and its background quad, and returns to title.

Do not infer successful model generation from `GET /health`: it confirms bridge responsiveness and configured provider/model only. A real conversation turn must be tested separately.

## Configuration and ownership

`BepInEx/config/amanatsu.ai-chat.cfg` is the source of truth for UI-editable settings. The plugin uses `BepInEx/config/amanatsu.ai-chat/bridge.json` as a generated/updated bridge mirror; advanced upstream `options` are preserved when the settings UI saves. Do not document or change `bridge.local.json` as the current installed authority. Read config files only as necessary and never expose secrets in reports.

| Section | Relevant keys / behavior |
| --- | --- |
| `UI` | `OpenOnStartup=false`; leave the normal title available. |
| `Character` | Optional `CardPath`, Mod-side profile `ConfigPath`. Character PNG is not rewritten by personality edits. The per-card profile stores `IdlePose`. |
| `Light` | Camera-parented character light: vertical/horizontal angle, intensity, color. Edited from the Light panel. |
| `Keybinds` | F10 title entry; 1/2 eye/neck toggles; H menu toggle; Shift camera unlock; Ctrl reset; configurable orbit/pan mouse buttons. Numeric and reset shortcuts are ignored while typing. |
| `Camera` | `DefaultPreset=portrait`, `OrbitSensitivity`, `PanSensitivity`, `SmoothingSeconds`, `FieldOfView`. |
| `Appearance` | `BackgroundImage=game:0` by default; `none`, `game:0..9`, or absolute `.png/.jpg/.jpeg`; `JapaneseFontFile`, `InputFontSize`, `InputBackgroundOpacity`. |
| `Graphics` | `FollowGameSettings=true` reads current game preferences on every entry; `false` uses saved Mod values. Includes native effect flags, `ShadowType`, and `BackgroundColor`. Native options may update them; temporary overrides are restored on exit. |
| `Bridge` | `Endpoint`, `BearerToken`, `BackendSettingsPath`, `Provider`, `UpstreamUrl`, `Model`, `ApiKey`, `LogThinking`. API keys and tokens are plain text. |
| `Testing` | `DiagnosticsEnabled=false` outside tests. Enabling creates local file-based command/state diagnostics on next game launch. |

`BackgroundBackdrop` scans `DefaultData/common/bg/al_mapsample*.png*` at runtime. On the tested installation it exposes 10 entries, but code uses the discovered count. A custom image must exist, be at most 32 MiB, and have dimensions at most 8192 px per axis. Selection writes `Appearance/BackgroundImage`; failure leaves the previous selection. The background is an unlit camera-facing quad behind the character, not an asset bundled into the Mod.

Personality editing changes only the per-card Mod profile under `BepInEx/config/amanatsu.ai-chat/characters`, with independent presets under `personalities`. The editable prompt is character behavior/style, not schema instructions. The distribution bundles ten personality presets (11–20) from `release/personalities`; the installer never overwrites an existing file of the same name. Do not treat an existing user profile or card as disposable test data.

Expression presets are distinct from personality profiles. At startup the Mod creates/loads `BepInEx/config/amanatsu.ai-chat/expression-presets.json`; saved entries are global across characters. Legacy per-card `Expressions` are ignored after binding and are not silently merged. A preset stores ID, Japanese description, brow/eyes/mouth pattern indices, blush (0..1), and optional `EyesOpen`/`MouthOpen` fixed rates (null = automatic blink/lip movement). The default file has 28 entries. Preview uses `HumanFace.Change*` APIs; save is atomic and updates the in-memory allow-list for the next LLM request. No card PNG is written. Motion labels are provided by `MotionCatalog`/`MOTION_HINTS`; per-character `MotionDescriptions` can override those labels. `turn_left`/`turn_right` are retired because the camera-facing stage left the character's back turned. Idle poses (`PoseList` Pose_D_00..11) are offered with descriptions and persist after the reply. The bridge receives both dynamic description maps and still constrains selected names by JSON Schema enum.

## Bridge contract and constraints

- Default provider: `ollama`; default model: `gemma4:e4b` (official Ollama library; uncensored models are only recommended, never bundled or defaulted); default upstream URL: `http://127.0.0.1:11434/api/chat`; default game-to-bridge endpoint: `http://127.0.0.1:38429/v1/chat`.
- The bridge uses Ollama's `format` JSON Schema or OpenAI-compatible `response_format.type=json_schema` to constrain structure when supported. `dialogue` is free text, while motion/expression/outfit values are restricted to advertised allow-lists. Do not silently fall back to prompt-only JSON on unsupported runtimes.
- A second programmatic validation checks required text, allowed values, and command limits. Control-like trailing text is sanitized; an empty natural dialogue may be regenerated once at lower temperature or rejected. Model/runtime schema support still needs per-backend testing.
- Ollama thinking is optional. If enabled and supplied by the model, it may enter bridge/BepInEx logs, not the heroine's dialogue. Never expose such logs without review.
- `BackendSettings.Save()` permits HTTP on loopback, but remote endpoints must use HTTPS. The UI restart is limited to the matching local bridge, requires a second click within 10 seconds, and must not stop a different process that occupies the port.
- Response schema field order is deliberate: `outfit_direction` (none/take_off/put_on/half_off), `requested_outfit`, `outfit`, `expression`, `motion`, `pose`, `dialogue`. Deciding the outfit before writing dialogue keeps agreement and action consistent. Individual eyebrow/eye/mouth indices are not in the schema (small models chose comic faces); the runner still accepts them for diagnostics.
- Outfit handling is entirely LLM-driven; there is no local regex/command parser. The AI sees verb labels (`wear_all`, `remove_all`, `half_off_all`, `show_bra`, `show_panties`, `underwear_only`, `topless`, `bottomless`, `remove_<part>`, `wear_<part>`, `half_off_<part>`) that `outfit_labels` in `bridge.py` maps back to game actions. Only actions that would change something right now are offered. `current_outfit` gives each part as e.g. `on(フリルビキニ)`.
- Prompt layout: rules and personality first, then a trailing `Current state:` block (current clothes and available clothing values) so the model server can reuse the cached prefix between turns.
- On truncation or repeat-limit stops, the bridge retries once with loosened limits and logs the truncated output. On a model change it unloads the previous Ollama model (`keep_alive: 0`). A missing model yields a readable 404 message; the UI offers a model picker (`ModelCatalog`) and a connection check that verifies the model exists (not generation). Thinking models should be used as non-thinking variants.
- Whole-outfit changes enumerate supported parts and verify each state instead of relying on `SetClothesStateAll`, which can leave unsupported slots unchanged.
- Named `preset:*` outfit actions provide seven target silhouettes: dressed, bra_show, panties_show, underwear, topless, bottomless, nude (`Sequence/OutfitPresets.cs`). All except `dressed` only remove clothing and never re-dress a part. Missing parts and unsupported states are skipped; the adapter verifies final states and restores an individually rejected slot.
- The coordinate button switches swimsuit/after-bath coordinates via `ChangeCoordinateTypeAndReload`, carrying per-part undress states over.
- BepInEx `[Conversation]` entries record accepted user input and response text as JSON-escaped single lines. Assistant records include `source=llm` or `source=llm-greeting`; these logs can contain private text.

The standalone ADV camera originally had no `Volume`. `ConversationPostProcessing` creates a stage-only volume (see the rendering notes below), reads current `GraphicData`, and restores camera state on exit. Fog/map/shield settings have no visual target in the standalone portrait stage.

## Build, package, and tests

Run from the game root:

```powershell
dotnet build .\ModSource\AiChat\AiChat.csproj -c Release --no-restore
dotnet run --project .\ModSource\AiChat\Tests\OutfitIntentSmoke.csproj -c Release --no-restore
dotnet run --project .\ModSource\AiChat\Tests\ExpressionPresetsSmoke.csproj -c Release
python -m unittest .\ModSource\AiChat\test_bridge.py .\ModSource\AiChat\test_restart_bridge.py
python .\ModSource\AiChat\test_catalog.py
./ModSource/AiChat/build-release.ps1
```

`build-release.ps1` creates both `dist/AmanatsuAiChat-<version>.zip` (Japanese) and `dist/AmanatsuAiChat-EN-<version>.zip` (English), each with a SHA-256 manifest. It refuses to overwrite an existing release directory, so bump the version for every new build. Packaging does not install; run the packaged `install.ps1` only while the game is closed. The installer updates an existing installation in place (e.g. `BepInEx/plugins/SELF`), refuses when more than one `Amanatsu.AiChat.dll` exists under `plugins`, verifies copied hashes, backs up replaced Mod files and copies bundled presets only when no file of the same name exists. The uninstaller moves every found copy to a backup.

The repository ships only tests that run without the game: `test_bridge.py`, `test_restart_bridge.py`, `test_catalog.py` (needs the game's asset bundles and UnityPy) and the C# smoke tests in `Tests/` (build the two projects separately; they share the folder). Game-integration checks use `[Testing] DiagnosticsEnabled=true` and the file-based commands in `DiagnosticsFile.cs`/`Plugin.ExecuteTestCommand`; they are maintainer tools and are not published. Never drive a user's card in such checks without permission, and disable diagnostics afterwards.

0.7.3 counts wrapped lines instead of total width: a page holds at most three lines of about 34 full-width characters, an explicit newline starts a new line, and the "▼" marker is counted; blank text gives one empty page; a key press and a click in the same frame turn only one page. 0.7.2 splits long replies into pages of about three lines in the native dialogue window (`Sequence/DialoguePages.cs`: sentence ends first, then clauses, then width; a trailing "▼" marks more pages); a left click on the dialogue window, Space or PageDown (while no input field is focused) shows the next page. The Bust camera preset now aims lower (target height 0.84 instead of 0.88) so the face sits above the dialogue window. 0.7.1 adds deleting expression presets from the panel (second press within 10 seconds; the last preset cannot be deleted), a `default` preset (all zero, auto openness) and detailed expression editing docs; the demo falls back to any preset when `smile` was deleted. 0.7.0 is the first public release. Beyond the features above it sets the official `gemma4:e4b` as the default model (uncensored models are only recommended in the manuals) and adds an Advanced panel in the connection settings for `[Bridge] MaxReplyTokens`, `ContextTokens` (0 = provider default: Ollama 512/4096, OpenAI-compatible 512/16384) and `HistoryMessages` (default 30). They are mirrored to `bridge.json` as `max_reply_tokens`, `context_tokens`, `history_messages`, override hand-written `options.num_ctx`/`num_predict`, and set OpenAI `max_tokens`; the game keeps max(60, HistoryMessages) messages. The model picker uses the typed API key, pages long lists and hides non-chat models; the bridge learns per model when an OpenAI-style API rejects `max_tokens` (switching to `max_completion_tokens`, at least 4096) or `temperature`. Verified in the game: Japanese and English editions with a local Ollama model, and Japanese chat plus a clothing request with GPT-6 luna over the OpenAI API. Every card/outfit/resolution/other-Mod combination remains untested.

## Logs and handoff

- Game/BepInEx: `BepInEx/LogOutput.log`.
- UI-restarted bridge: `BepInEx/config/amanatsu.ai-chat/bridge-logs`.
- Optional diagnostics: `BepInEx/config/amanatsu.ai-chat/diagnostics` (keep disabled for users).
- `MOTION_INVENTORY.md` distinguishes verified safe dialogue motions from additional game assets that may require other contexts or props.

When modifying behavior, update both this README and the human manual, verify the packaged copies, and state precisely which tests were performed. Never claim that build/package success implies a tested installation or model conversation.


## Rendering, face and light notes

- The game's look comes from Beautify (a URP renderer feature + `Beautify.Universal.Beautify` volume component) plus URP Bloom and ShadowsMidtonesHighlights on the map volumes (layer 3). `AL.Config.ConfigEffectorVolume.Refresh` is an empty method in this build. `ConversationPostProcessing` rebuilds the map005 morning profile on layer 29 and maps `Manager.Config.GraphicData` flags onto it (Beautify bloom/DoF, URP Bloom, URP Vignette, the `ScreenSpaceAmbientOcclusion` renderer feature).
- The character light mirrors the map CameraLight prefab: a directional light parented to the conversation camera, culling layers 7 and 9, soft shadows. `[Light]` cfg entries store vertical/horizontal angle, intensity and color.
- Eye/mouth openness uses `FBSBase.SetFixedRate` on `face.eyesCtrl` / `face.mouthCtrl`. The game default (auto) is `-0.1`; fixed eyes also disable blinking. `ChangeEyesOpenMax` etc. only cap the range and are reset to 1 (mouth min 0). Presets store `EyesOpen` / `MouthOpen` (null = auto).
- Diagnostics-only commands (besides title/stage/outfit/sequence tests): `dump_volumes <path>`, `screenshot <path>`, `face_adjust part,delta`, `light part,delta|reset`, `menu true|false`, `coordinate`, `pose <delta>`, `model_picker`, `test_connection`.
