# Amanatsu AI Chat — User Manual (English edition)

For Amanatsu AI Chat 0.7.1. This is an unofficial mod for *Amanatsu Location* (甘夏ろけーしょん). The normal game still starts from the title screen as usual; the chat screen opens from the "Amanatsu AI Chat v0.7.1" button on the title screen.

This file is for players. Implementation, testing and AI-agent notes are in the accompanying `README-AI.md`.

In the English edition, the mod's own screens, the AI instructions, the expression and pose descriptions and the bundled personality presets are all in English, and the character replies in English. The game's own screens (the title screen, the options window and the character names on cards) stay as the game shows them.

## 1. Requirements

- *Amanatsu Location* with BepInEx 6. Use the "Unity IL2CPP" build for "Windows x64"; BepInEx 5 and the Mono builds do not work. Tested with game 1.0.2 / BepInEx 6.0.0-be.788.
- Windows and Python 3.10 or later. Check with `python --version`. Python runs the "bridge" that passes messages between the game and the AI. You don't write any code; you only run the commands in this manual.
  - With the installer from python.org, tick "Add python.exe to PATH" on the first screen. Without it, the `python` command is not found.
  - If typing `python` opens the Microsoft Store, Python is not installed or not on PATH. Reinstall it.
- Ollama, or an OpenAI-compatible chat API. Ollama is the program that runs AI models on your PC; install it from ollama.com. By default the mod uses Ollama with the official `gemma4:e4b`. Official models may refuse sexual requests or requests to undress, so if you want that kind of conversation, we recommend installing an uncensored model. The model itself is not included and is downloaded separately with Ollama; check the license of any model you use. For English conversation, a model that is good at English role-play works best.
- A female character card (PNG) to talk with. Cards are not included.

The game, character cards, models, API keys, fonts and other mods are not included in the ZIP.

### How heavy it is

Local models are heavy. They share the GPU with the game, so on an RTX 3060 Laptop (6 GB of GPU memory) a single reply can take close to a minute. If you have little GPU memory, choose a smaller model. If your GPU is not powerful enough, you can use an API such as OpenAI's instead (paid).

Example model download sizes. A model larger than your GPU memory runs partly on the CPU and gets even slower.

| Model | Size |
| --- | --- |
| `gemma4:e4b` (default) | about 9.6 GB |
| Qwen3 8B (Q4_K_M) | about 5.0 GB |
| Gemma 4 E4B (Q4_K_M) | about 6.3 GB |

## 2. Installation

The examples below assume the game is at `C:\ILLGAMES\AmanatsuLocation`. Replace the path if yours is different.

1. Close the game. A running game locks the DLL.
2. Extract the ZIP and open the `AmanatsuAiChat-EN-0.7.1` folder.
3. Open PowerShell in that folder and run `python -m pip install -r .\BepInEx\plugins\AmanatsuAiChat\requirements.txt`.
4. In the same place, run `./install.ps1 -GameRoot 'C:\ILLGAMES\AmanatsuLocation'`.
   - If an earlier version is in a folder under `BepInEx/plugins` (for example `plugins/SELF`), that copy is updated. Otherwise the mod goes into `BepInEx/plugins`.
   - Replaced files are backed up to `BepInEx/config/amanatsu.ai-chat/backups` in the game folder. Settings, cards and saves are not overwritten.
   - If `Amanatsu.AiChat.dll` exists in more than one place, it would load twice, so the installer stops and lists the locations. Keep only one and run it again.
5. If you use Ollama, start it and download a model, for the default model: `ollama pull gemma4:e4b`. Models are large, so check your free space and allow time.
6. Start the game. If "Amanatsu AI Chat v0.7.1" appears on the title screen, the mod is loaded.

If PowerShell refuses to run `install.ps1` because of the execution policy, read the error and allow it temporarily for trusted files only. You do not need to lower your security settings permanently.

## 3. Your first conversation

1. Click "Amanatsu AI Chat v0.7.1" on the title screen. The first time, a dialog asks for a character card; choose the PNG card to talk with. Cancel leaves you on the title screen. The card is remembered and opens automatically next time.
2. When the character and the pink dialogue box appear, open "Connection" on the left panel.
3. Check the provider and the LLM API URL, then use "Choose…" next to the model name to pick an installed model. Press "Save & apply". With a local Ollama you normally do not need an API key.
4. Press "Restart bridge" twice within 10 seconds and wait for "Restarted". This restarts only this mod's local bridge; if it is not running yet, it simply starts.
5. Press "Test connection". It checks that the bridge answers and that the chosen model is installed. It does not make the model generate text, so finish by trying one short message.

When the chat screen opens, and whenever you switch characters, the character speaks first. If you send a message while the greeting is being generated, she drops the greeting and answers you instead.

Reply speed depends heavily on the model size and your PC. Without a GPU, a reply can take minutes. "Stop" cancels a reply in progress. "Exit" on the left panel ends the conversation and returns to the normal title screen.

## 4. Screen and controls

| Control | What it does |
| --- | --- |
| "Amanatsu AI Chat v0.7.1" on the title screen | Opens the chat screen. The game's own start button stays available. |
| "Prev char" / "Next char" | Cycles through the cards in `UserData/chara/female`. |
| "Open card…" | Picks a PNG card with the Windows file dialog. |
| "◀ Pose ▶" next to the name | Switches the idle standing pose (Pose 01-12). The choice is saved per character and she returns to it after motions. |
| "Outfit: swimsuit / after bath" | Switches between the swimsuit and after-bath outfits. Undressed and half-undressed states carry over part by part; parts missing from the new outfit stay as they are. |
| "Face" / "Bust" / "Full" | Camera framing. The mouse wheel zooms. |
| Left-drag while holding Shift | Orbits the camera. Right- or middle-drag pans. |
| Ctrl | Resets the camera. |
| 1 / 2 | Switches eyes / neck between following the camera and following the animation. |
| Brow / Eyes / Mouth `+` / `-` | Changes each face part individually. |
| "Blush" / "Eyes open" / "Mouth open" | `+` / `-` fixes the strength or openness (0.0-1.0). "Auto" returns eyes and mouth to blinking and lip movement. |
| Expression preset controls ("Prev", "Next", "New", "Save current face", "Delete") | Create, edit and delete the shared expression presets. See "Creating, editing and deleting expression presets" in section 5. |
| "Light" | Adjusts the character light's vertical and horizontal angle, intensity and color (Windows color picker). "Reset to default" is included. Settings are saved. |
| "Shadow" | Cycles the chat light's shadows: none, hard, soft. |
| "Options" | Opens the game's own options window. Closing it returns to the chat. |
| Background "Prev" / "Next" / "Choose image…" | Uses a plain color, a background bundled with the game, or your own PNG/JPEG. Your image is not copied into the mod. |
| "－" at the top right / "Menu" at the top left / H | Hides or shows the control panel. |
| "Exit" | Ends the conversation and returns to the normal title screen. |

Keys can be changed in the config file. Shortcuts such as 1, 2, H and Ctrl do not work while a text box has the cursor. F10 also opens and closes the chat screen.

### What the AI controls

With every reply, the AI picks the following to match the conversation:

- **Expression**: one of the shared expression presets (29 by default). It does not combine brows, eyes and mouth on its own.
- **Motion**: stretching, gazing into the distance and similar movements. "Turn left/right" is not offered, because on the face-to-face chat screen it would leave her with her back to you.
- **Standing pose**: one of 12 poses, chosen by mood or on request ("cross your arms"). The pose stays after the reply and is saved per character.
- **Clothing**: see "Asking about clothes" below.

### Asking about clothes

Ask in your own words: "take it off", "just the top", "put your bra and panties on", "get naked", "show me your butt". The AI reads the request, and the clothes change only if the character agrees. If she refuses, nothing changes.

- Every time, the AI is told which parts she is wearing and what each garment is called (for example "Frill Bikini"), so requests such as "take off your swimsuit" work.
- Whole states are also available: dressed, showing the bra, showing the panties, underwear only, topless, bottomless and naked. The undressing states only remove clothes and never put a part back on; only "dressed" puts everything back on.
- Choices that cannot change anything right now (for example "show the bra" when she wears no top) are not shown to the AI.
- Only the displayed outfit state changes. The card image and outfit data are never modified. Leaving the chat or switching characters restores the original outfit.

### Conversation memory

The conversation is remembered only while the game is running. If you close the chat screen and reopen it with the same character, you can continue; switching characters or quitting the game forgets it. Nothing is saved to disk. In long conversations the oldest exchanges are dropped to fit what the model can handle. "Advanced…" in "Connection" sets how much is sent.

## 5. Personality, expressions and connection

"Personality…" edits how the character talks, her personality and how she treats you. "Apply to card" saves it to the mod's own settings for that card; the card PNG itself is never modified. "Save as…" saves just the personality to a separate file, and "Open file…" loads one; after loading, press "Apply to card". Output format and motion control cannot be changed here.

The ZIP includes ten English personality presets: caretaker, tomboy, graceful, ojousama, diligent, gal, big sis, devoted, quiet and tsundere. The installer copies them to `BepInEx/config/amanatsu.ai-chat/personalities-en`; pick one with "Open file…" and press "Apply to card". Files with the same name are never overwritten. The English edition keeps its own folders, so Japanese presets never appear in the list.

### Creating, editing and deleting expression presets

The expressions the AI chooses are shared "expression presets". There are 29 by default, and you can create, edit and delete them with the expression controls near the bottom of the control panel. The AI receives each preset's ID and description and chooses by reading the description, so describe when the face should be used, not only how it looks.

**Create a new one**
1. Press "New" in the expression controls. The ID and description fields are cleared.
2. Make the face on the panel: pick brow, eye and mouth shapes with `+` / `-`, and set "Blush", "Eyes open" and "Mouth open" with `+` / `-`. Leave eyes and mouth on "Auto" to keep blinking and lip movement from the animation.
3. Enter an ID (starts with a lowercase letter; lowercase letters, digits and `_`, up to 40 characters) and a description for the AI (one line, up to 160 characters).
4. Press "Save current face".

**Edit an existing one**
1. Choose the expression with "Prev" / "Next". The face is shown and its ID and description fill the fields.
2. Adjust the face or the description.
3. Press "Save current face" with the same ID to overwrite it. Saving under a different ID adds a new expression and keeps the original.

**Delete one**
1. Choose the expression with "Prev" / "Next".
2. Press "Delete", then press it again within 10 seconds. A confirmation message appears after the first press.
3. The last remaining preset cannot be deleted. The 29 defaults can be deleted too, but there is no way to restore them, so be careful.

The English edition saves presets to `BepInEx/config/amanatsu.ai-chat/expression-presets-en.json`, shared by all characters. Changes apply from the next reply. How blush looks depends on the character's own materials and settings.

"Connection" sets the provider (Ollama or an OpenAI-compatible API), bridge URL, LLM API URL, model name, API key and whether Ollama's thinking is logged. Choosing the model with "Choose…" avoids typos. The list is fetched with the API key currently in the field, so it works before you save. Long lists have "Prev page" / "Next page" buttons, and models that cannot chat (audio, image, embedding and so on) are left out of OpenAI-compatible lists. Remote APIs require HTTPS. With thinking logging on, the model's reasoning may be written to the log, so keep it off for private conversations.

### Finding the LLM API URL (Ollama)

If Ollama runs on this PC, keep the default LLM API URL `http://127.0.0.1:11434/api/chat`.

- To check that Ollama is running, open `http://127.0.0.1:11434` in a browser. It shows "Ollama is running" when it is.
- If you changed the port with the `OLLAMA_HOST` environment variable, replace `11434` with that port.
- To use Ollama running on another PC, the URL must start with `https://`.
- After entering the URL, press "Choose…". If your installed models appear, the URL is set correctly.

To use the OpenAI API, set the provider to "OpenAI-compatible API", set the LLM API URL to `https://api.openai.com/v1/chat/completions` and enter your API key. If a newer model rejects `max_tokens` or `temperature`, the bridge switches how it sends them automatically. API usage is billed to your OpenAI account.

"Advanced…" in "Connection" changes the reply limit, the context length and how many past messages are sent to the model. You normally don't need to touch them. Empty fields use the defaults (512 reply tokens; context 4096 tokens for Ollama and 16384 for APIs; 30 messages, or 15 exchanges). How much of the conversation the character remembers is mostly set by the context length. With Ollama, a larger value remembers more but uses more GPU memory and gets slower. After changing them, press "Save & apply" and "Restart bridge".

Models that "think" before answering can be slow or get cut off. For Gemma 4, Qwen3 and similar families, choose a variant with thinking turned off. If a reply is cut off, the bridge retries once with more room.

The main settings file is `BepInEx/config/amanatsu.ai-chat.cfg` in the game folder. Connection settings are also mirrored to `BepInEx/config/amanatsu.ai-chat/bridge.json`. Close the game before editing settings files by hand; the running game overwrites them when it saves. API keys and tokens are stored as plain text, so check your settings and logs before sharing them.

Main manual settings:

| Setting | Purpose |
| --- | --- |
| `[UI] Language` | `auto` (default) follows the installed edition. `ja` or `en` forces a language. |
| `[UI] OpenOnStartup` | Default `false`. `true` opens the chat screen right after the game starts. |
| `[Character] CardPath` | The last card you chose. Leave it empty to get the file dialog next time. |
| `[Appearance] BackgroundImage` | `none`, `game:0`-`game:9`, or an absolute path to a PNG/JPEG. Images must be 32 MB or smaller and at most 8192 px per side. |
| `[Appearance] JapaneseFontFile` / `InputFontSize` / `InputBackgroundOpacity` | Font, size and background opacity of the input box. |
| `[Camera] DefaultPreset` / `SmoothingSeconds` / `FieldOfView` | Initial framing, camera smoothing and field of view. |
| `[Keybinds]` | F10, 1, 2, H, Shift, Ctrl and mouse button assignments. |
| `[Graphics] FollowGameSettings` | Default `true`: takes the game's graphics settings each time a chat starts. `false` uses the values saved by the mod. |
| Other `[Graphics]` entries | Shadow type, bloom, depth of field, vignette and so on for the chat screen. Changing them in the game's "Options" also applies to the chat screen. |
| `[Light]` | Direction, intensity and color of the character light. Normally change these from the "Light" panel. |
| `[Bridge]` | Endpoint, model, authentication, thinking log and the advanced values (`MaxReplyTokens`, `ContextTokens`, `HistoryMessages`). Normally change these from "Connection". |

The chat screen is rendered with the same finishing as the game's maps (tone mapping, color grading, sharpening, bloom, depth of field and so on) and follows the game's bloom, depth of field, vignette, SSAO and anti-aliasing options. The character light follows the camera as in the main game and casts soft shadows. Fog, map and shield settings have no visible effect on a screen that shows only the character and a background image. The background image also receives color grading and bloom, so bright images may look washed out.

## 6. Troubleshooting

| Symptom | What to check |
| --- | --- |
| No button on the title screen | Check that BepInEx is installed, that exactly one `Amanatsu.AiChat.dll` exists under `BepInEx/plugins`, and that `BepInEx/LogOutput.log` shows no load errors. |
| Cannot choose a card / no character appears | Check that you chose a female character card PNG and that the file still exists. |
| "Model … was not found" | The model name is wrong or the model is not installed. Pick one again with "Choose…" in "Connection". |
| The model list fails (HTTP 401) | The API key is empty or wrong. Enter it again, then press "Choose…". |
| "Connection failed" / no reply | Check that Ollama or your API server is running and that the bridge URL, port and token are right, then press "Restart bridge" and "Test connection". |
| Replies get cut off or are very slow | Check that you are not using a thinking model. The game shares your GPU, so generation is slower while the game is running. |
| She replies in Japanese | Check that `[UI] Language` is `auto` or `en` and that the character's personality text is in English. A Japanese personality applied to a card makes the model answer in Japanese. |
| Clothes don't change as expected | Some outfits do not support states such as half-undressed for every part. Try another wording, or a larger model if you use a small one. |
| The background doesn't appear | Check that the image file was not moved or deleted. Compare with a game background first. |
| Text is hard to read | Adjust `JapaneseFontFile`, `InputFontSize` and `InputBackgroundOpacity` in the cfg and restart the game. |
| The bridge fails to restart | Check whether another program is using the same port. The mod never stops unrelated processes. |

The game log is `BepInEx/LogOutput.log`; logs of a bridge restarted from the screen are in `BepInEx/config/amanatsu.ai-chat/bridge-logs`. `[Conversation]` lines record your input and the character's replies; model replies are tagged `llm` and the opening line `llm-greeting`. Logs contain conversation text and connection details, so check them before sharing.

## 7. Updating, removing and what was tested

To update, close the game and run `install.ps1` from the new package. To remove the mod, close the game and run `./uninstall.ps1 -GameRoot 'C:\ILLGAMES\AmanatsuLocation'` in the extracted folder. It moves only the mod's files to a recoverable backup and keeps settings, logs, cards and saves. Stop the bridge separately if it is still running.

The English edition shares the Japanese edition's code. The English screens, the opening line and a reply from a real model were checked in the game; Japanese conversation and a clothing request over the OpenAI API (GPT-6 luna) were also checked; the other features were tested in the game on the Japanese edition. Not every card, outfit, resolution or combination with other mods has been tested. After updating the game or BepInEx, check that everything still works.
