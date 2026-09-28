import re
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch
import bridge


class BridgeTests(unittest.TestCase):
    def test_mock_explicit(self):
        self.assertTrue(bridge.Bridge(True).complete({"user_text": "test"})["sequence"])

    def test_missing_config_is_not_mock(self):
        with patch.dict("os.environ", {}, clear=True):
            with self.assertRaises(RuntimeError):
                bridge.Bridge(False).complete({"user_text": "test"})

    def test_live_config_reload_and_api_key(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict("os.environ", {}, clear=True):
            path = Path(directory) / "bridge.json"
            path.write_text(json.dumps({"provider": "ollama", "url": "http://localhost:11434/api/chat",
                                        "model": "first", "api_key": "secret-a"}), encoding="utf-8")
            client = bridge.Bridge(False, settings=json.loads(path.read_text(encoding="utf-8")), settings_path=path)
            self.assertEqual(client.api_key, "secret-a")
            path.write_text(json.dumps({"provider": "chat-completions", "url": "https://example.test/v1/chat/completions",
                                        "model": "second", "api_key": "secret-b"}), encoding="utf-8")
            client.refresh()
            self.assertEqual((client.provider, client.model, client.api_key),
                             ("chat-completions", "second", "secret-b"))

    def test_ollama_thinking_on_and_off(self):
        payload = {"message": {"content": '{"dialogue":"hello","expression":"none","motion":"none","outfit":"none"}', "thinking": "model-provided reasoning"}}
        response = MagicMock()
        response.read.return_value = json.dumps(payload).encode("utf-8")
        context = MagicMock()
        context.__enter__.return_value = response
        for enabled in (True, False):
            with self.subTest(enabled=enabled), patch("bridge.urllib.request.urlopen", return_value=context) as open_url:
                client = bridge.Bridge(False, settings={"provider": "ollama", "url": "http://localhost:11434/api/chat",
                                                        "model": "test", "log_thinking": enabled})
                result = client.complete({"user_text": "test"})
                request_data = json.loads(open_url.call_args.args[0].data)
                self.assertEqual(request_data["think"], enabled)
                self.assertEqual(result.get("thinking"), "model-provided reasoning" if enabled else None)
                self.assertEqual(result["sequence"], [{"type": "text", "value": "hello"}])

    def test_dialogue_protocol_fragment_is_retried(self):
        def context(dialogue):
            response = MagicMock()
            response.read.return_value = json.dumps({"message": {"content": json.dumps({
                "dialogue": dialogue, "expression": "none", "motion": "none", "outfit": "none"
            })}}).encode("utf-8")
            manager = MagicMock()
            manager.__enter__.return_value = response
            return manager

        with patch("bridge.urllib.request.urlopen", side_effect=[context("expression:"), context("はい、覚えています。")]) as open_url:
            client = bridge.Bridge(False, settings={"provider": "ollama", "url": "http://localhost:11434/api/chat",
                                                    "model": "test", "options": {"temperature": 0.3}})
            result = client.complete({"user_text": "test"})
            self.assertEqual(result["sequence"][-1]["value"], "はい、覚えています。")
            self.assertEqual(open_url.call_count, 2)
            retried = json.loads(open_url.call_args.args[0].data)
            self.assertEqual(retried["options"]["temperature"], 0.0)

    def test_trailing_protocol_fragment_is_removed_without_retry(self):
        self.assertEqual(bridge.spoken_dialogue("ええ、もちろん。」「expression:"), ("ええ、もちろん。", True))
        self.assertEqual(bridge.spoken_dialogue("普通のセリフです。"), ("普通のセリフです。", False))

    def test_empty_input(self):
        with self.assertRaises(ValueError):
            bridge.Bridge(True).complete({"user_text": " "})

    def test_history(self):
        messages = bridge.build_prompt({"history": [{"role": "user", "text": "以前の発言"}], "user_text": "続き"})
        self.assertEqual(messages[-2], {"role": "user", "content": "以前の発言"})
        self.assertEqual(messages[-1]["content"], "続き")

    def test_history_bounded(self):
        messages = bridge.build_prompt({"history": [{"role": "user", "text": "x"}] * 100})
        self.assertEqual(len(messages), 32)

    def test_ollama_json(self):
        self.assertEqual(bridge.extract_json_content({"message": {"content": '{"dialogue":"hello"}'}}), {"dialogue": "hello"})

    def test_compatible_json(self):
        self.assertEqual(bridge.extract_json_content({"choices": [{"message": {"content": '```json\n{"dialogue":"hello"}\n```'}}]}), {"dialogue": "hello"})

    def test_invalid_json(self):
        with self.assertRaises(ValueError):
            bridge.extract_json_content({"message": {"content": '[]'}})

    def test_command_strings_rejected(self):
        with self.assertRaises(ValueError):
            bridge.extract_json_content({"message": {"content": '{"dialogue":'}})

    def test_schema_limits_and_allowlist(self):
        schema = bridge.sequence_schema({"available_motions": ["idle", "stretch"]})
        properties = schema["properties"]
        self.assertIn("dialogue", schema["required"])
        self.assertEqual(properties["motion"]["enum"], ["idle", "stretch"])
        self.assertNotIn("eyebrow", properties)
        self.assertNotIn("eyes", properties)
        self.assertNotIn("mouth", properties)
        self.assertEqual(properties["outfit"]["enum"], ["none", "remove_all", "wear_all"])

    def test_half_undress_only_when_available(self):
        body = {"available_outfit_actions": ["undress", "half_undress", "dress"]}
        outfit = bridge.sequence_schema(body)["properties"]["outfit"]
        self.assertEqual(outfit["enum"], ["none", "remove_all", "half_off_all", "wear_all"])
        self.assertIn("half_off_all=半脱ぎ", bridge.build_prompt(body)[0]["content"])

    def test_part_outfit_actions_are_limited_to_available_states(self):
        body = {"available_outfit_actions": ["undress", "dress", "top:undress", "top:dress", "bra:half_undress"]}
        outfit = bridge.sequence_schema(body)["properties"]["outfit"]
        self.assertEqual(outfit["enum"], ["none", "remove_all", "wear_all", "remove_top", "wear_top", "half_off_bra"])
        prompt = bridge.build_prompt(body)[0]["content"]
        self.assertIn("never substitute a whole-outfit value for a part", prompt)

    def test_named_outfit_presets_are_schema_constrained(self):
        presets = ["preset:dressed", "preset:bra_show", "preset:panties_show",
                   "preset:underwear", "preset:topless", "preset:bottomless", "preset:nude"]
        body = {"available_outfit_actions": presets}
        outfit = bridge.sequence_schema(body)["properties"]["outfit"]
        self.assertEqual(outfit["enum"], ["none", "wear_all", "show_bra", "show_panties", "underwear_only",
                                          "topless", "bottomless", "remove_all"])
        self.assertIn("remove_all=全部脱ぐ/全裸", bridge.build_prompt(body)[0]["content"])

    def test_labels_map_back_to_game_actions(self):
        labels = bridge.outfit_labels(["preset:dressed", "preset:nude", "undress", "top:undress", "bra:half_undress"])
        self.assertEqual(labels["wear_all"], "preset:dressed")
        self.assertEqual(labels["remove_all"], "preset:nude")
        self.assertEqual(labels["remove_top"], "top:undress")
        self.assertEqual(labels["half_off_bra"], "bra:half_undress")

    def test_no_unsupported_gestures_in_prompt(self):
        text = bridge.build_prompt({"available_motions": ["idle", "stretch"]})[0]["content"]
        self.assertIn("Allowed motions: idle=待機, stretch=伸び", text)

    def test_editable_expression_and_motion_descriptions_reach_prompt(self):
        body = {"available_expressions": ["gentle_blush"],
                "expression_descriptions": {"gentle_blush": "赤面しながら優しく微笑む"},
                "available_motions": ["stretch"],
                "motion_descriptions": {"stretch": "大きく背伸びする"}}
        text = bridge.build_prompt(body)[0]["content"]
        self.assertIn("gentle_blush=赤面しながら優しく微笑む", text)
        self.assertIn("stretch=大きく背伸びする", text)

    def test_outfit_requires_explicit_request_in_prompt(self):
        text = bridge.build_prompt({})[0]["content"]
        self.assertIn("Never change clothes when nothing was requested", text)

    def test_personality_is_separate_from_fixed_protocol(self):
        text = bridge.build_prompt({"system_prompt": "落ち着いた口調で話す。"})[0]["content"]
        self.assertIn("The following protocol rules are fixed", text)
        self.assertIn("Editable character personality and speaking style:\n落ち着いた口調で話す。", text)



class ContextAndAuthTests(unittest.TestCase):
    def test_history_is_trimmed_to_context_keeping_rules_and_latest(self):
        body = {"user_text": "今", "system_prompt": "性格", "available_expressions": ["smile"],
                "available_motions": ["idle"],
                "history": [{"role": "user" if i % 2 == 0 else "assistant", "text": f"{i}番目" + "あ" * 300}
                            for i in range(30)]}
        messages = bridge.build_prompt(body, 4096, 384)
        self.assertEqual(messages[0]["role"], "system")
        self.assertIn("性格", messages[0]["content"])
        self.assertEqual(messages[-1]["content"], "今")
        self.assertLess(len(messages), 32)
        self.assertTrue(messages[-2]["content"].startswith("29番目"))
        used = sum(bridge.estimate_tokens(m["content"]) for m in messages)
        self.assertLessEqual(used, 4096 - 384)

    def test_bridge_token_is_enforced_only_when_configured(self):
        open_bridge = bridge.Bridge(mock=True)
        self.assertTrue(open_bridge.authorized(None))
        locked = bridge.Bridge(mock=True, settings={"bridge_token": "secret"})
        self.assertFalse(locked.authorized(None))
        self.assertFalse(locked.authorized("Bearer wrong"))
        self.assertTrue(locked.authorized("Bearer secret"))


class StrictSchemaTests(unittest.TestCase):
    def test_face_keys_required_only_for_strict_backends(self):
        body = {"available_expressions": ["smile"], "available_motions": ["idle"]}
        self.assertNotIn("eyes", bridge.sequence_schema(body)["required"])
        strict = bridge.sequence_schema(body, strict=True)
        self.assertEqual(sorted(strict["required"]), sorted(strict["properties"]))


class QuotedFragmentTests(unittest.TestCase):
    def test_quoted_key_fragment_is_removed(self):
        text, stripped = bridge.spoken_dialogue('まぁまぁかな。あたしは好きだけど。」「expression":"neutral"')
        self.assertTrue(stripped)
        self.assertEqual(text, 'まぁまぁかな。あたしは好きだけど。')


class LanguageTests(unittest.TestCase):
    BODY = {"user_text": "hi", "system_prompt": "P", "available_expressions": ["smile"],
            "available_motions": ["idle"], "available_poses": ["pose_wave"],
            "available_outfit_actions": ["undress", "top:undress"], "current_outfit": {"top": "on(Shirt)"}}

    def test_english_prompt_has_no_japanese_and_asks_for_english(self):
        system = bridge.build_prompt(dict(self.BODY, language="en"))[0]["content"]
        self.assertIn("Write dialogue in English", system)
        self.assertIsNone(re.search(r"[぀-ヿ一-鿿]", system))

    def test_japanese_is_the_default(self):
        system = bridge.build_prompt(self.BODY)[0]["content"]
        self.assertIn("Write dialogue in Japanese", system)
        self.assertIn("服を脱いで", system)


class OpenAiParameterTests(unittest.TestCase):
    def test_rejected_max_tokens_and_temperature_are_adapted(self):
        import io, urllib.error
        b = bridge.Bridge(False, {"provider": "chat-completions", "url": "https://x/v1/chat/completions", "model": "m"})
        good = {"choices": [{"finish_reason": "stop", "message": {"content": json.dumps({
            "outfit_direction": "none", "requested_outfit": "none", "outfit": "none", "expression": "smile",
            "motion": "idle", "pose": "none", "dialogue": "やあ"})}}]}
        errors = [b"Unsupported parameter: 'max_tokens' is not supported with this model. Use 'max_completion_tokens' instead.",
                  b"Unsupported value: 'temperature' does not support 0.3 with this model."]
        sent = []
        def fake(req, timeout):
            sent.append(json.loads(req.data))
            if errors:
                raise urllib.error.HTTPError(req.full_url, 400, "Bad Request", {}, io.BytesIO(errors.pop(0)))
            m = MagicMock(); m.__enter__.return_value.read.return_value = json.dumps(good).encode()
            return m
        with patch("urllib.request.urlopen", fake):
            r = b.complete({"user_text": "こんにちは", "available_expressions": ["smile"], "available_motions": ["idle"]})
        self.assertEqual(len(sent), 3)
        self.assertNotIn("max_tokens", sent[2])
        self.assertGreaterEqual(sent[2]["max_completion_tokens"], 4096)
        self.assertNotIn("temperature", sent[2])
        self.assertTrue(any(c["type"] == "text" and c["value"] == "やあ" for c in r["sequence"]))
        # The next turn starts with the learned parameters.
        with patch("urllib.request.urlopen", fake):
            b.complete({"user_text": "またね", "available_expressions": ["smile"], "available_motions": ["idle"]})
        self.assertEqual(len(sent), 4)
        self.assertNotIn("max_tokens", sent[3])


class AdvancedSettingsTests(unittest.TestCase):
    def test_defaults_follow_provider(self):
        self.assertEqual(bridge.Bridge(False, {"provider": "ollama"}).context_limits(), (4096, 512))
        self.assertEqual(bridge.Bridge(False, {"provider": "chat-completions"}).context_limits(), (16384, 512))

    def test_advanced_values_override_options(self):
        b = bridge.Bridge(False, {"provider": "ollama", "max_reply_tokens": 1024, "context_tokens": 8192,
                                  "options": {"num_ctx": 4096, "num_predict": 384, "num_gpu": 0}})
        self.assertEqual(b.context_limits(), (8192, 1024))

    def test_history_limit(self):
        history = [{"role": "user" if i % 2 == 0 else "assistant", "text": f"m{i}"} for i in range(40)]
        body = {"user_text": "hi", "history": history}
        self.assertEqual(len(bridge.build_prompt(body, 100000, 512, 10)), 12)
        self.assertEqual(len(bridge.build_prompt(body, 100000, 512, 0)), 2)


class RetryTests(unittest.TestCase):
    def test_truncated_output_is_retried_with_more_room(self):
        b = bridge.Bridge(False, {"provider": "ollama", "url": "http://x/api/chat", "model": "m",
                                  "options": {"num_predict": 100}})
        good = {"message": {"content": json.dumps({"requested_outfit": "none", "outfit": "none", "expression": "smile",
                                                   "motion": "idle", "pose": "none", "dialogue": "やあ"})}, "done_reason": "stop"}
        replies = [{"message": {"content": "", "thinking": "..."}, "done_reason": "length"}, good]
        sent = []
        def fake(req, timeout):
            sent.append(json.loads(req.data))
            m = MagicMock(); m.__enter__.return_value.read.return_value = json.dumps(replies.pop(0)).encode()
            return m
        with patch("urllib.request.urlopen", fake):
            r = b.complete({"user_text": "こんにちは", "available_expressions": ["smile"], "available_motions": ["idle"]})
        self.assertEqual(len(sent), 2)
        # Hidden reasoning used the budget, so the retry gets room for it, and later turns start there.
        self.assertEqual(sent[1]["options"]["num_predict"], bridge.THINKING_BUDGET)
        self.assertTrue(any(c["type"] == "text" and c["value"] == "やあ" for c in r["sequence"]))
        self.assertFalse(sent[0]["think"])
        self.assertIn("m", b.thinking_models)

if __name__ == "__main__":
    unittest.main()
