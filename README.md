# Amanatsu AI Chat

[English](README.en.md) | 日本語

『甘夏ろけーしょん』用の非公式Modです。専用の会話画面で、キャラクターとローカルLLM（Ollama、またはOpenAI互換API）を使って自由に会話できます。AIは返事に合わせて、表情・モーション・立ちポーズ・服装を選びます。

- 対象バージョン: 0.7.0（日本語版・英語版）
- 動作確認環境: ゲーム 1.0.2 / BepInEx 6.0.0-be.788（IL2CPP）
- ライセンス: MIT（[LICENSE](LICENSE)）

このModはILLGAMESとは無関係の非公式Modです。ゲーム本体、キャラカード、LLMモデル、フォントは含みません。

## 使いたい方へ

配布ZIPは、このリポジトリの [Releases](https://github.com/xl6tzl15/amanatsu-ai-chat/releases/latest) からダウンロードできます。日本語版は `AmanatsuAiChat-<版>.zip`、英語版は `AmanatsuAiChat-EN-<版>.zip` です。導入方法・操作方法・困ったときの対処は、ユーザーマニュアル [release/README-JA.md](release/README-JA.md) を参照してください。

## ソースからビルドする方へ

プロジェクトはゲームの参照アセンブリ（BepInEx と `interop-AmanatsuLocation`）を相対パスで参照します。このリポジトリは、ゲームフォルダーの `ModSource/AiChat` に置いてください。

```
<ゲームフォルダー>/
  BepInEx/                  # BepInEx 6 IL2CPP（interop-AmanatsuLocation を生成済み）
  ModSource/AiChat/         # このリポジトリ
```

必要なもの:

- .NET 6 SDK
- Python 3.10 以降（`pip install -r requirements.txt`）
- 一度ゲームを起動して、BepInEx に `BepInEx/interop-AmanatsuLocation` を生成させておくこと

ゲームフォルダーで次を実行すると、DLLのビルドと配布ZIPの作成ができます。

```powershell
dotnet build .\ModSource\AiChat\AiChat.csproj -c Release
./ModSource/AiChat/build-release.ps1
```

日本語版と英語版の配布ZIPが `ModSource/AiChat/dist/` にできます。テストの実行方法、ブリッジの仕様、設計上の決まりごとは、開発者・AIエージェント向けの [README-AI.md](README-AI.md) にまとめています。

## 構成

| パス | 内容 |
| --- | --- |
| `Plugin.cs`, `Config/`, `UI/`, `GameAdapter/`, `Sequence/`, `LLM/` | BepInEx プラグイン本体（C#） |
| `bridge.py`, `restart_bridge.py` | ゲームとLLMの間を取り持つローカルHTTPブリッジ |
| `release/` | ユーザーマニュアル（日本語・英語）、インストーラー、同梱の性格プリセット（`personalities`・`personalities-en`） |
| `Localization.cs` | 日本語・英語の切り替え |
| `build-release.ps1` | 配布ZIPの作成 |
| `Tests/`, `test_*.py` | テスト（ゲームを使うものは診断モードが必要） |
