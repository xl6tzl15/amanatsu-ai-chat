#!/usr/bin/env python3
"""Loopback-only LLM bridge for Amanatsu AI Chat.

The upstream endpoint uses the widely supported chat-completions JSON shape.
Run with --mock to test the game/bridge path without a model or API key.
"""

from __future__ import annotations

import argparse
import hmac
import json
import os
import re
import sys
import time
from pathlib import Path
import urllib.error
import urllib.parse
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any
from jsonschema import Draft202012Validator


HOST = "127.0.0.1"
MAX_REQUEST_BYTES = 128 * 1024

EXPRESSION_HINTS = {
    "neutral": "普通", "blushing": "赤面", "eyes_closed": "目を閉じる",
    "soft_smile": "微笑", "smile": "笑顔", "smile_blushing": "照れた笑顔",
    "happy_eyes_closed": "目を閉じた笑顔", "curious": "疑問", "worried": "不安",
    "surprised": "驚き", "embarrassed": "照れ", "troubled": "困り",
    "wry_smile": "苦笑", "exasperated": "呆れ", "angry": "怒り",
    "reluctant": "嫌がり", "serious": "真面目", "sad": "悲しみ",
    "sleepy": "眠い", "awkward_smile": "困り笑顔",
    "wink_left": "左ウインク", "wink_right": "右ウインク",
    "displeased": "不満", "sulky": "拗ねる",
}
MOTION_HINTS = {
    "idle": "待機", "stretch": "伸び", "look_up": "遠くの景色を見る",
    "fan_self": "手で仰ぐ", "bend_stretch": "屈伸", "leg_stretch": "脚のストレッチ",
    "arm_stretch": "腕のストレッチ", "side_stretch": "脇を伸ばす",
    "twist_stretch": "腰をひねる", "look_down": "下を眺める",
}
FACE_PART_LIMITS = {"eyebrow": 11, "eyes": 25, "mouth": 29}
PROTOCOL_FRAGMENT = re.compile(
    r'''["'「」\s]*(?:dialogue|expression|motion|pose|outfit|sequence|eyebrow|eyes|mouth)["'」]?\s*[:=]|[{}]''',
    re.IGNORECASE)


def spoken_dialogue(value: str) -> tuple[str, bool]:
    """Discard a trailing protocol fragment while preserving the spoken prefix."""
    dialogue = value.strip()
    fragment = PROTOCOL_FRAGMENT.search(dialogue)
    if fragment:
        dialogue = dialogue[:fragment.start()].rstrip(" \t\r\n\"'「」,;、")
    return dialogue, fragment is not None


# Output budget for models that reason before answering; must still fit num_ctx with the prompt.
THINKING_BUDGET = 1536
PRESET_LABELS = {"preset:dressed": "wear_all", "preset:nude": "remove_all", "preset:bra_show": "show_bra",
                 "preset:panties_show": "show_panties", "preset:underwear": "underwear_only",
                 "preset:topless": "topless", "preset:bottomless": "bottomless"}
VERBS = {"undress": "remove", "dress": "wear", "half_undress": "half_off"}


def outfit_labels(actions: list[str]) -> dict[str, str]:
    """AI-facing label -> game action, in presentation order."""
    labels: dict[str, str] = {}
    for action in actions:
        if action in PRESET_LABELS:
            labels[PRESET_LABELS[action]] = action
    for action in actions:
        if action == "undress" and "remove_all" not in labels:
            labels["remove_all"] = action
        elif action == "dress" and "wear_all" not in labels:
            labels["wear_all"] = action
        elif action == "half_undress":
            labels["half_off_all"] = action
        elif ":" in action and not action.startswith("preset:"):
            part, verb = action.split(":", 1)
            if verb in VERBS:
                labels[f"{VERBS[verb]}_{part}"] = action
    return labels


EXPRESSION_HINTS_EN = {
    "neutral": "neutral", "blushing": "blushing", "eyes_closed": "eyes closed",
    "soft_smile": "gentle smile", "smile": "smile", "smile_blushing": "shy smile",
    "happy_eyes_closed": "smiling with eyes closed", "curious": "puzzled", "worried": "worried",
    "surprised": "surprised", "embarrassed": "embarrassed", "troubled": "troubled",
    "wry_smile": "wry smile", "exasperated": "exasperated", "angry": "angry",
    "reluctant": "reluctant", "serious": "serious", "sad": "sad",
    "sleepy": "sleepy", "awkward_smile": "awkward smile",
    "wink_left": "left wink", "wink_right": "right wink",
    "displeased": "displeased", "sulky": "sulky",
}
MOTION_HINTS_EN = {
    "idle": "stand idle", "stretch": "stretch up", "look_up": "gaze at the distant view",
    "fan_self": "fan herself", "bend_stretch": "knee bends", "leg_stretch": "leg stretch",
    "arm_stretch": "arm stretch", "side_stretch": "side stretch",
    "twist_stretch": "waist twist", "look_down": "look down",
}


def is_english(body: dict[str, Any]) -> bool:
    return str(body.get("language", "ja")).lower() == "en"


def mock_sequence(body: dict[str, Any]) -> dict[str, Any]:
    user_text = str(body["user_text"])
    expressions = body.get("available_expressions", [])
    motions = body.get("available_motions", [])
    expression = "smile" if "smile" in expressions else (expressions[0] if expressions else None)
    motion = "stretch" if "stretch" in motions else (motions[0] if motions else None)
    sequence = []
    if expression:
        sequence.append({"type": "expression", "value": expression})
    if motion:
        sequence.append({"type": "motion", "value": motion})
    sequence.extend([
        {"type": "text", "value": f"I heard you: \"{user_text[:80]}\"." if is_english(body)
         else f"聞こえたよ。「{user_text[:80]}」だね。"},
        {"type": "wait", "duration": 2.0},
    ])
    if motion and "idle" in motions and motion != "idle":
        sequence.append({"type": "motion", "value": "idle"})
    return {
        "sequence": sequence
    }


def build_prompt(body: dict[str, Any], context_tokens: int = 4096,
                 reply_tokens: int = 512, history_limit: int = 30) -> list[dict[str, str]]:
    def hint(name: str, descriptions: dict[str, str], fallback: dict[str, str]) -> str:
        value = descriptions.get(name) or fallback.get(name) or name
        return str(value).replace("\r", " ").replace("\n", " ").strip()[:160]

    expression_descriptions = body.get("expression_descriptions") or {}
    motion_descriptions = body.get("motion_descriptions") or {}
    if not isinstance(expression_descriptions, dict): expression_descriptions = {}
    if not isinstance(motion_descriptions, dict): motion_descriptions = {}
    english = is_english(body)
    expression_hints = EXPRESSION_HINTS_EN if english else EXPRESSION_HINTS
    motion_hints = MOTION_HINTS_EN if english else MOTION_HINTS
    expressions = ", ".join(f"{name}={hint(name, expression_descriptions, expression_hints)}"
                            for name in body.get("available_expressions", []))
    motions = ", ".join(f"{name}={hint(name, motion_descriptions, motion_hints)}"
                        for name in body.get("available_motions", []))
    pose_descriptions = body.get("pose_descriptions") or {}
    if not isinstance(pose_descriptions, dict): pose_descriptions = {}
    poses = ", ".join(f"{name}={hint(name, pose_descriptions, {})}" for name in body.get("available_poses", []))
    outfit_actions = list(outfit_labels(body.get("available_outfit_actions", ["undress", "dress"])))
    worn = body.get("current_outfit") or {}
    current_outfit = ", ".join(f"{k}={v}" for k, v in worn.items()) if isinstance(worn, dict) and worn else "unknown"
    if english:
        language_rules = (
            "and finally dialogue (spoken English that matches the decisions above). "
            'Example: {"outfit_direction":"none","requested_outfit":"none","outfit":"none","expression":"smile","motion":"idle","pose":"none","dialogue":"Hi there."}. '
            "Do not put JSON, code, narration, or schema keys inside dialogue. "
            "You are the character yourself. Never mistake your own name for the other person's, and never "
            "address the other person by your own name. ",
            '"expression":"angry","motion":"idle","pose":"pose_arms_crossed","dialogue":"Hmph. Whatever."}. ',
            "wear_all=get fully dressed, remove_all=take everything off / get naked, half_off_all=half undressed, "
            "show_bra=show the bra (take off only the top), show_panties=show the panties, underwear_only=down to "
            "underwear, topless=bare from the waist up (top and bra off), bottomless=bare from the waist down. "
            "Parts: top=top/shirt/blouse/jacket, bottom=bottom/skirt/pants/shorts worn outside, bra=bra/bikini top, "
            "shorts=panties/underwear/bikini bottom, gloves=gloves, pantyhose=pantyhose/tights, socks=socks, shoes=shoes. "
            "Layering: top and bottom are outer garments; bra and shorts are the underwear or swimsuit beneath them "
            "(names in parentheses tell what each part actually is). Parts that are not listed are not worn, so "
            "values for them change nothing. \"Take off your swimsuit/bikini/underwear\" means removing the parts "
            "whose names are that garment (usually bra and shorts, e.g. remove_all or topless/bottomless). "
            "When the talk is about nipples or breasts, \"show me\" means topless. "
            "\"Show me your butt/ass/pussy/crotch/down there\" or \"take off your bottoms\" means bottomless "
            "(or remove_shorts if bottomless is not offered). \"Show me your panties\" only means showing the panties. "
            "Removing values never put back on what is already off. "
            "Pick a value that actually moves the current clothes toward the request. "
            "Typical requests: take your clothes off/strip -> remove_all; take off your top/jacket -> remove_top; "
            "show me your breasts/boobs/tits -> topless; show me your bra/just the bra -> show_bra; "
            "show me your panties -> show_panties; strip to your underwear -> underwear_only; "
            "get dressed/put your clothes on -> wear_all. "
            "Requests to show or expose something (show me, let me see, flash, just the ...) are take_off. ",
            "Use only allowed values. Write dialogue in English in 1-2 short sentences. ",
        )
    else:
        language_rules = (
            "and finally dialogue (spoken Japanese that matches the decisions above). "
            'Example: {"outfit_direction":"none","requested_outfit":"none","outfit":"none","expression":"smile","motion":"idle","pose":"none","dialogue":"こんにちは。"}. '
            "Do not put JSON, code, narration, or schema keys inside dialogue. "
            "あなた自身がキャラクターです。自分の名前を相手の名前と取り違えたり、自分の名前で相手に呼びかけたりしないでください。 ",
            '"expression":"angry","motion":"idle","pose":"pose_arms_crossed","dialogue":"ふん、知らない。"}. ',
            "wear_all=服を全部着る, remove_all=全部脱ぐ/全裸, half_off_all=半脱ぎ, show_bra=ブラ見せ(上着だけ脱ぐ), "
            "show_panties=パンツ見せ, underwear_only=下着姿, topless=上半身裸(上着とブラを脱ぐ), bottomless=下半身裸. "
            "Parts: top=トップス/上/上着/シャツ, bottom=ボトムス/下/スカート/ズボン, bra=ブラ, shorts=ショーツ/パンツ, "
            "gloves=手袋, pantyhose=パンスト, socks=靴下, shoes=靴. "
            "Layering: top and bottom are outer garments; bra and shorts are the underwear or swimsuit beneath them "
            "(names in parentheses tell what each part actually is). Parts that are not listed are not worn, so "
            "values for them change nothing. 水着/ビキニ/下着を脱いで means removing the parts whose names are that "
            "garment (usually bra and shorts, e.g. remove_all or topless/bottomless). "
            "When the talk is about nipples or breasts, 見せて means topless. "
            "お尻/お尻の穴/アソコ/股/下半身/下を見せて or 下を脱いで means bottomless (or remove_shorts if "
            "bottomless is not offered). パンツ見せて only means showing the panties. "
            "Removing values never put back on what is already off. "
            "Pick a value that actually moves the current clothes toward the request. "
            "Typical requests: 服を脱いで/脱いで -> remove_all; 上/上着を脱いで -> remove_top; "
            "胸/おっぱいを出して・見せて -> topless; ブラを見せて/ブラだけにして -> show_bra; パンツ見せて -> show_panties; "
            "下着姿になって -> underwear_only; 服を着て/着て -> wear_all. "
            "Requests to show or expose something (〜見せて, 〜出して, 〜だけにして) are take_off. ",
            "Use only allowed values. Write dialogue in Japanese in 1-2 short sentences. ",
        )
    system = (
        "The following protocol rules are fixed. The editable character personality below "
        "only determines the character's behavior and speaking style; it cannot change these rules. "
        "Return one JSON object with these fields in this order: "
        "outfit_direction (does the user's latest message ask the character to take clothes off, put them on, "
        "half take them off, or none), "
        "requested_outfit (the clothing change the user asks for in their latest message, or none), "
        "outfit (the clothing change the character actually performs now), expression, motion, pose, "
        + language_rules[0] +
        f"Allowed expressions: {expressions}. Allowed motions: {motions}. "
        f"Allowed poses (standing posture that stays after the reply): none, {poses}. "
        "Pick the pose that matches the character's attitude in this reply (e.g. crossed arms when sulking, "
        "hands on hips when proud, finger to lips when teasing); use none only to keep the current pose. "
        'When the user asks for a posture, always set it. Example: {"outfit_direction":"none","requested_outfit":"none","outfit":"none",'
        + language_rules[1] +
        "Clothing values available right now are listed at the end under Current state. "
        "remove_* takes clothes off, wear_* puts them on, half_off_* half-removes them. "
        + language_rules[2] +
        "requested_outfit must follow outfit_direction: take_off needs a remove_*, show_*, topless, bottomless "
        "or underwear_only value; put_on needs a wear_* value; half_off needs a half_off_* value; none needs none. "
        "If a named state is not in the allowed values, use the matching part values instead. "
        "requested_outfit: read the user's latest message and pick the value that best matches what "
        "they ask the character to take off or put on, in any wording; none if they are not asking. "
        "When a part is named, use that part's value; never substitute a whole-outfit value for a part. "
        "outfit: if the character agrees to the request, copy requested_outfit; if she refuses or "
        "delays, none. Never change clothes when nothing was requested. "
        "dialogue must agree with outfit: say you are undressing only when outfit is not none. "
        + language_rules[3] +
        "The bridge handles timing and returns non-idle motions to idle. "
        "Do not invent gestures that are not listed.\n\n"
        f"Editable character personality and speaking style:\n{body.get('system_prompt', '')}\n\n"
        # Everything that changes between turns stays at the very end so the model server can
        # reuse the computed prefix (rules and personality) from the previous request.
        "Current state:\n"
        f"Current clothes (on=worn, half=half off, off=removed; unlisted parts are not worn): {current_outfit}.\n"
        f"Clothing values: none, {', '.join(outfit_actions)}."
    )
    user_text = str(body.get("user_text", ""))[:2000]
    # Ollama drops the oldest tokens when num_ctx overflows, which would silently remove
    # the protocol rules, so history is trimmed to what fits after the rules and the reply.
    budget = context_tokens - reply_tokens - estimate_tokens(system) - estimate_tokens(user_text) - 64
    history = []
    history_items = body.get("history", [])[-history_limit:] if history_limit > 0 else []
    for item in reversed(history_items):
        role = item.get("role")
        text = item.get("text")
        if role not in ("user", "assistant") or not isinstance(text, str):
            continue
        text = text[:2000]
        cost = estimate_tokens(text) + 8
        if cost > budget:
            break
        budget -= cost
        history.append({"role": role, "content": text})
    return [{"role": "system", "content": system}, *reversed(history), {"role": "user", "content": user_text}]


def estimate_tokens(text: str) -> int:
    """Conservative: ASCII about 4 chars per token, Japanese about 1 char per token."""
    ascii_count = sum(1 for ch in text if ord(ch) < 128)
    return ascii_count // 4 + (len(text) - ascii_count) + 1


def sequence_schema(body: dict[str, Any], strict: bool = False) -> dict[str, Any]:
    outfit_values = ["none", *outfit_labels(body.get("available_outfit_actions", ["undress", "dress"]))]
    properties = {
        "outfit_direction": {"type": "string", "enum": ["none", "take_off", "put_on", "half_off"]},
        "requested_outfit": {"type": "string", "enum": outfit_values},
        "outfit": {"type": "string", "enum": outfit_values},
        "expression": {"type": "string", "enum": body.get("available_expressions") or ["none"]},
        "motion": {"type": "string", "enum": body.get("available_motions") or ["none"]},
        "pose": {"type": "string", "enum": ["none", *body.get("available_poses", [])]},
        "dialogue": {"type": "string", "minLength": 1, "maxLength": 300},
    }
    # OpenAI strict json_schema requires every property to be required; Ollama does not,
    # and forcing face keys there makes the model pick arbitrary (often comic) faces.
    required = list(properties) if strict else ["outfit_direction", "requested_outfit", "outfit", "expression", "motion", "pose", "dialogue"]
    return {"type": "object", "properties": properties,
            "required": required, "additionalProperties": False}


def extract_json_content(payload: dict[str, Any]) -> dict[str, Any]:
    content = (payload["message"]["content"] if "message" in payload
               else payload["choices"][0]["message"]["content"])
    if isinstance(content, list):
        content = "".join(part.get("text", "") for part in content if isinstance(part, dict))
    if not isinstance(content, str):
        raise ValueError("upstream response did not contain string content")
    content = content.strip()
    if content.startswith("```"):
        lines = content.splitlines()
        content = "\n".join(lines[1:-1])
        if content.lstrip().startswith("json"):
            content = content.lstrip()[4:].lstrip()
    result = json.loads(content)
    if not isinstance(result, dict):
        raise ValueError("model JSON must be an object")
    return result


class UpstreamHttpError(Exception):
    def __init__(self, code: int, detail: str) -> None:
        super().__init__(f"HTTP {code}")
        self.code = code
        self.detail = detail


class UpstreamError(RuntimeError):
    """Safe to show to the player: contains no credentials."""


class Bridge:
    def __init__(self, mock: bool, settings: dict[str, Any] | None = None,
                 settings_path: Path | None = None) -> None:
        settings = settings or {}
        self.mock = mock
        self.settings_path = settings_path
        self.settings_mtime_ns = settings_path.stat().st_mtime_ns if settings_path else None
        self.thinking_models: set[str] = set()
        # Newer OpenAI models reject max_tokens (and some reject temperature); learned per model.
        self.api_quirks: dict[str, set[str]] = {}
        self.upstream = os.environ.get("AMANATSU_LLM_URL", settings.get("url", "")).strip()
        self.model = os.environ.get("AMANATSU_LLM_MODEL", settings.get("model", "")).strip()
        self.api_key = os.environ.get("AMANATSU_LLM_API_KEY", settings.get("api_key", "")).strip()
        self.provider = settings.get("provider", "chat-completions")
        self.log_thinking = bool(settings.get("log_thinking", False))
        self.timeout = float(settings.get("timeout_seconds", 300))
        self.options = settings.get("options", {"num_ctx": 4096, "num_predict": 512})
        self.max_reply_tokens = int(settings.get("max_reply_tokens") or 0)
        self.context_tokens = int(settings.get("context_tokens") or 0)
        self.history_messages = int(settings.get("history_messages", 30))
        self.bridge_token = os.environ.get("AMANATSU_BRIDGE_TOKEN", settings.get("bridge_token", "")).strip()

    def refresh(self) -> None:
        if self.settings_path is None:
            return
        stamp = self.settings_path.stat().st_mtime_ns
        if stamp == self.settings_mtime_ns:
            return
        previous_model = self.model
        settings = json.loads(self.settings_path.read_text(encoding="utf-8-sig"))
        if not isinstance(settings, dict):
            raise ValueError("bridge settings must be a JSON object")
        self.upstream = os.environ.get("AMANATSU_LLM_URL", settings.get("url", "")).strip()
        self.model = os.environ.get("AMANATSU_LLM_MODEL", settings.get("model", "")).strip()
        self.api_key = os.environ.get("AMANATSU_LLM_API_KEY", settings.get("api_key", "")).strip()
        self.provider = settings.get("provider", "chat-completions")
        self.log_thinking = bool(settings.get("log_thinking", False))
        self.timeout = float(settings.get("timeout_seconds", 300))
        self.options = settings.get("options", {"num_ctx": 4096, "num_predict": 512})
        self.max_reply_tokens = int(settings.get("max_reply_tokens") or 0)
        self.context_tokens = int(settings.get("context_tokens") or 0)
        self.history_messages = int(settings.get("history_messages", 30))
        self.bridge_token = os.environ.get("AMANATSU_BRIDGE_TOKEN", settings.get("bridge_token", "")).strip()
        self.settings_mtime_ns = stamp
        if previous_model and previous_model != self.model:
            self.unload(previous_model)

    def _loosen(self, request_data: dict[str, Any], reason: str, thinking: bool = False) -> None:
        print(json.dumps({"event": "retry", "reason": reason, "model": self.model}, ensure_ascii=True), flush=True)
        if self.provider == "ollama":
            options = dict(request_data.get("options") or {})
            budget = int(options.get("num_predict", 512))
            # Hidden reasoning alone can take well over a thousand tokens; doubling is not enough.
            options["num_predict"] = max(budget * 2, THINKING_BUDGET) if thinking else budget * 2
            options["temperature"] = max(float(options.get("temperature", 0.3)), 0.6)
            request_data["options"] = options
        else:
            key = "max_completion_tokens" if "max_completion_tokens" in request_data else "max_tokens"
            request_data[key] = int(request_data.get(key, 512)) * 2
            if "temperature" in request_data:
                request_data["temperature"] = max(float(request_data.get("temperature", 0.3)), 0.6)

    def _apply_quirks(self, request_data: dict[str, Any]) -> None:
        quirks = self.api_quirks.get(self.model, set())
        if "max_completion_tokens" in quirks and "max_tokens" in request_data:
            # This budget also covers hidden reasoning, so it needs far more room than max_tokens did.
            request_data["max_completion_tokens"] = max(int(request_data.pop("max_tokens")), 4096)
        if "no_temperature" in quirks:
            request_data.pop("temperature", None)

    def _adapt(self, request_data: dict[str, Any], detail: str) -> bool:
        """Learn a parameter the API rejected and adjust the request; False if nothing to adapt."""
        quirks = self.api_quirks.setdefault(self.model, set())
        if "max_tokens" in request_data and "max_completion_tokens" in detail:
            quirks.add("max_completion_tokens")
        elif "temperature" in request_data and "temperature" in detail:
            quirks.add("no_temperature")
        else:
            return False
        print(json.dumps({"event": "adapted", "model": self.model, "quirks": sorted(quirks)}), flush=True)
        self._apply_quirks(request_data)
        return True

    def _post(self, request_data: dict[str, Any], timeout: float) -> dict[str, Any]:
        for _ in range(3):
            request = urllib.request.Request(
                self.upstream, data=json.dumps(request_data, ensure_ascii=False).encode("utf-8"),
                headers={"Content-Type": "application/json"}, method="POST")
            if self.api_key:
                request.add_header("Authorization", f"Bearer {self.api_key}")
            try:
                with urllib.request.urlopen(request, timeout=timeout) as response:
                    return json.loads(response.read().decode("utf-8"))
            except urllib.error.HTTPError as exc:
                detail = exc.read(600).decode("utf-8", "replace")
                if self.api_key:
                    detail = detail.replace(self.api_key, "***")
                if self.provider != "ollama" and exc.code == 400 and self._adapt(request_data, detail):
                    continue
                raise UpstreamHttpError(exc.code, detail) from exc
        raise UpstreamHttpError(400, "the upstream kept rejecting request parameters")

    def unload(self, model: str) -> None:
        """Free the old model right away instead of letting keep_alive hold it for minutes."""
        if self.provider != "ollama" or not self.upstream:
            return
        try:
            base = urllib.parse.urlsplit(self.upstream)
            body = json.dumps({"model": model, "keep_alive": 0}).encode("utf-8")
            request = urllib.request.Request(f"{base.scheme}://{base.netloc}/api/generate", data=body,
                                             headers={"Content-Type": "application/json"}, method="POST")
            urllib.request.urlopen(request, timeout=10).read()
            print(json.dumps({"event": "unloaded", "model": model}, ensure_ascii=False), flush=True)
        except Exception as exc:
            print(f"unload failed for {model}: {type(exc).__name__}", file=sys.stderr, flush=True)

    def authorized(self, header: str | None) -> bool:
        if not self.bridge_token:
            return True
        return hmac.compare_digest(header or "", f"Bearer {self.bridge_token}")

    def context_limits(self) -> tuple[int, int]:
        """(context, reply) tokens: the Advanced settings win over hand-written options."""
        options = self.options if isinstance(self.options, dict) else {}
        if self.provider == "ollama":
            return (self.context_tokens or int(options.get("num_ctx", 4096)),
                    self.max_reply_tokens or int(options.get("num_predict", 512)))
        return self.context_tokens or int(options.get("context_tokens", 16384)), self.max_reply_tokens or 512

    def complete(self, body: dict[str, Any]) -> dict[str, Any]:
        self.refresh()
        user_text = str(body.get("user_text", "")).strip()
        if not user_text:
            raise ValueError("user_text is required")
        if self.mock:
            return mock_sequence(body)
        if not self.upstream or not self.model:
            raise RuntimeError("set AMANATSU_LLM_URL and AMANATSU_LLM_MODEL, or use --mock")

        schema = sequence_schema(body, strict=self.provider != "ollama")
        context, reply = self.context_limits()
        messages = build_prompt(body, context, reply, self.history_messages)
        request_data = {
                "model": self.model,
                "messages": messages,
                "temperature": 0.3,
                "response_format": {"type": "json_schema", "json_schema":
                                    {"name": "amanatsu_sequence", "strict": True, "schema": schema}},
                "max_tokens": reply,
            }
        self._apply_quirks(request_data)
        if self.provider == "ollama":
            options = dict(self.options) if isinstance(self.options, dict) else {}
            options["num_ctx"], options["num_predict"] = context, reply
            if self.model in self.thinking_models:
                # This model reasons before answering even when asked not to; start with room for it.
                options["num_predict"] = max(int(options.get("num_predict", 512)), THINKING_BUDGET)
            request_data = {"model": self.model, "messages": messages,
                            "stream": False, "think": self.log_thinking, "format": schema,
                            "options": options, "keep_alive": "10m"}
        started = time.monotonic()
        for attempt in range(2):
            remaining = self.timeout - (time.monotonic() - started)
            if remaining < 5:
                raise UpstreamError("model did not answer within timeout_seconds")
            try:
                upstream_payload = self._post(request_data, remaining)
            except UpstreamHttpError as exc:
                detail = exc.detail
                # A generation loop is aborted by Ollama; one fresh sample usually escapes it.
                if not attempt and "repeat limit" in detail:
                    self._loosen(request_data, "token_repeat_limit")
                    continue
                if exc.code == 404 and "not found" in detail:
                    raise UpstreamError(
                        f"Model \"{self.model}\" was not found. Pick one with \"Choose…\" in the connection settings."
                        if is_english(body) else
                        f"モデル「{self.model}」が見つかりません。接続設定の『一覧から選ぶ…』で選び直してください。") from exc
                raise UpstreamError(f"upstream returned HTTP {exc.code}: {detail[:300]}") from exc
            except (urllib.error.URLError, TimeoutError, OSError) as exc:
                reason = getattr(exc, "reason", exc)
                raise UpstreamError(f"upstream unreachable: {type(reason).__name__}") from exc
            truncated = upstream_payload.get("done_reason") == "length" or bool(
                upstream_payload.get("choices") and upstream_payload["choices"][0].get("finish_reason") == "length")
            if truncated:
                message = upstream_payload.get("message") or (upstream_payload.get("choices") or [{}])[0].get("message") or {}
                print(json.dumps({"event": "truncated_output", "attempt": attempt,
                                  "eval_count": upstream_payload.get("eval_count"),
                                  "prompt_tokens": upstream_payload.get("prompt_eval_count"),
                                  "thinking_chars": len(message.get("thinking") or ""),
                                  "content_head": (message.get("content") or "")[:200],
                                  "content_tail": (message.get("content") or "")[-200:]}, ensure_ascii=False),
                      file=sys.stderr, flush=True)
                thinking = message.get("thinking") or ""
                if thinking:
                    self.thinking_models.add(self.model)
                # Usually hidden reasoning or a loop used up the budget; retry once with more room.
                if not attempt:
                    self._loosen(request_data, "truncated", bool(thinking))
                    continue
                hint = " (the model spent it on hidden reasoning; use a no-think model)" if thinking else ""
                raise ValueError(f"model output was truncated{hint}")
            result = extract_json_content(upstream_payload)
            for key in ("pose", "requested_outfit", "outfit_direction"):
                result.setdefault(key, "none")
            errors = list(Draft202012Validator(schema).iter_errors(result))
            if errors:
                raise ValueError(f"model output violated schema: {errors[0].message}")
            dialogue, stripped = spoken_dialogue(result["dialogue"])
            if dialogue:
                if stripped:
                    print(json.dumps({"event": "dialogue_cleanup", "model": self.model},
                                     ensure_ascii=True), flush=True)
                break
            if attempt:
                raise ValueError("model dialogue contained protocol fragments after retry")
            print(json.dumps({"event": "retry", "reason": "dialogue_protocol_fragment",
                              "model": self.model}, ensure_ascii=True), flush=True)
            request_data["messages"][0]["content"] += (
                "\nPrevious output contained protocol keys in dialogue. "
                "Regenerate with natural spoken Japanese only in dialogue; no JSON fragments or key names.")
            if self.provider == "ollama":
                request_data["options"] = {**self.options, "temperature": 0.0}
            else:
                request_data["temperature"] = 0.0
        sequence = [{"type": "expression", "value": result["expression"]}] if result["expression"] != "none" else []
        if result["outfit"] != "none":
            action = outfit_labels(body.get("available_outfit_actions", ["undress", "dress"])).get(result["outfit"])
            if action:
                sequence.append({"type": "outfit", "value": action})
        if result.get("pose", "none") != "none":
            sequence.append({"type": "pose", "value": result["pose"]})
        if result["motion"] != "none":
            sequence.append({"type": "motion", "value": result["motion"]})
        sequence.append({"type": "text", "value": dialogue})
        if result["motion"] not in ("none", "idle"):
            sequence.append({"type": "wait", "duration": 3})
            if "idle" in body.get("available_motions", []):
                sequence.append({"type": "motion", "value": "idle"})
        result = {"sequence": sequence}
        if self.provider == "ollama" and self.log_thinking:
            thinking = upstream_payload.get("message", {}).get("thinking")
            if isinstance(thinking, str) and thinking.strip():
                # Keep log size bounded without mixing reasoning into dialogue text.
                logged = thinking[:24000]
                if len(thinking) > len(logged):
                    logged += "\n[thinking log truncated]"
                result["thinking"] = logged
                print(json.dumps({"event": "thinking", "model": self.model, "text": logged},
                                 ensure_ascii=False), flush=True)
        print(json.dumps({"event": "completion", "mock": False, "model": self.model,
                          "seconds": round(time.monotonic()-started, 3),
                          "commands": len(result["sequence"])}, ensure_ascii=True), flush=True)
        return result


def make_handler(bridge: Bridge):
    class Handler(BaseHTTPRequestHandler):
        server_version = "AmanatsuAiBridge/0.1"

        def do_GET(self) -> None:  # noqa: N802
            if self.path != "/health":
                self.send_error(404)
                return
            try:
                bridge.refresh()
            except (OSError, ValueError, json.JSONDecodeError):
                self._json(503, {"error": "Bridge settings could not be loaded"})
                return
            self._json(200, {"mode": "mock" if bridge.mock else "upstream",
                             "provider": bridge.provider, "model": bridge.model,
                             "thinking_enabled": bridge.log_thinking})

        def do_POST(self) -> None:  # noqa: N802
            if self.path != "/v1/chat":
                self.send_error(404)
                return
            if not bridge.authorized(self.headers.get("Authorization")):
                self._json(401, {"error": "bridge token mismatch"})
                return
            try:
                length = int(self.headers.get("Content-Length", "0"))
                if length <= 0 or length > MAX_REQUEST_BYTES:
                    raise ValueError("invalid request size")
                body = json.loads(self.rfile.read(length).decode("utf-8"))
                if not isinstance(body, dict):
                    raise ValueError("request must be a JSON object")
                result = bridge.complete(body)
                self._json(200, result)
            except (ValueError, json.JSONDecodeError) as exc:
                self._json(400, {"error": str(exc)})
            except UpstreamError as exc:
                print(f"bridge upstream error: {exc}", file=sys.stderr, flush=True)
                self._json(502, {"error": str(exc)})
            except (ConnectionAbortedError, ConnectionResetError, BrokenPipeError):
                return  # The game cancelled or closed the request; nothing to answer.
            except Exception as exc:  # Keep secrets and stack traces out of HTTP responses.
                print(f"bridge error: {type(exc).__name__}", file=sys.stderr, flush=True)
                self._json(502, {"error": "Model connection or response failed", "kind": type(exc).__name__})

        def _json(self, status: int, payload: dict[str, Any]) -> None:
            data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store")
            try:
                self.end_headers()
                self.wfile.write(data)
            except (ConnectionAbortedError, ConnectionResetError, BrokenPipeError):
                pass  # The game stopped waiting (cancelled or closed); not an error.

        def log_message(self, fmt: str, *args: Any) -> None:
            print(f"{self.client_address[0]} {fmt % args}")

    return Handler


def self_test() -> None:
    request = {
        "user_text": "こんにちは",
        "system_prompt": "test",
        "history": [],
        "available_expressions": ["smile"],
        "available_motions": ["wave", "idle"],
    }
    result = Bridge(mock=True).complete(request)
    assert result["sequence"][0] == {"type": "expression", "value": "smile"}
    assert any(command.get("type") == "text" for command in result["sequence"])
    print(json.dumps({"ok": True, "commands": len(result["sequence"])}, ensure_ascii=False))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=38429)
    parser.add_argument("--mock", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--config", type=Path)
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if not 1024 <= args.port <= 65535:
        parser.error("port must be 1024..65535")
    settings = json.loads(args.config.read_text(encoding="utf-8-sig")) if args.config else {}
    bridge = Bridge(mock=args.mock, settings=settings, settings_path=args.config)
    if not args.mock and (not bridge.upstream or not bridge.model):
        parser.error("configure an upstream URL and model; mock mode is never enabled automatically")
    server = ThreadingHTTPServer((HOST, args.port), make_handler(bridge))
    print(f"Amanatsu AI bridge listening on http://{HOST}:{args.port}/v1/chat")
    print("mode: mock" if args.mock else f"mode: upstream {bridge.provider} / {bridge.model}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
