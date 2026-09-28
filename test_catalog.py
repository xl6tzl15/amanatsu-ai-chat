"""Cross-check exposed presets against the shipped game asset bundles."""
import json
import re
from pathlib import Path

import UnityPy

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path(__file__).resolve().parent
CONFIG = json.loads((ROOT / "BepInEx/config/amanatsu.ai-chat/default.json").read_text(encoding="utf-8-sig"))

EXPRESSION_ROWS = {
    "neutral": 0, "blushing": 1, "eyes_closed": 2, "soft_smile": 4,
    "smile": 10, "smile_blushing": 11, "happy_eyes_closed": 12,
    "curious": 14, "worried": 15, "surprised": 16, "embarrassed": 17,
    "troubled": 18, "wry_smile": 20, "exasperated": 21, "angry": 23,
    "reluctant": 26, "serious": 28, "sad": 29, "sleepy": 30,
    "awkward_smile": 33, "wink_left": 39, "wink_right": 40,
    "displeased": 41, "sulky": 43,
}
# Deliberately tuned away from the game table: stronger blush, and sleepy as half-open eyes.
TUNED = {"blushing": (0, 0, 0, .5), "smile_blushing": (2, 3, 2, .5), "embarrassed": (5, 2, 23, .5),
         "sleepy": (0, 0, 0, 0.0)}


def main():
    expressions = CONFIG["Expressions"]
    assert set(expressions) == set(EXPRESSION_ROWS)
    bundle = UnityPy.load(str(ROOT / "lib/com/lis/exp/000_00.unity3d"))
    tables = {data["m_Name"]: data["list"] for obj in bundle.objects
              if obj.type.name == "MonoBehaviour" for data in [obj.read_typetree()]}
    assert set(tables) == {f"c{i:02}" for i in range(12)}
    source = (SOURCE / "GameAdapter/ExpressionCatalog.cs").read_text(encoding="utf-8-sig")
    for name, index in EXPRESSION_ROWS.items():
        preset = expressions[name]
        expected = (preset["Eyebrow"], preset["Eyes"], preset["Mouth"], preset["Blush"])
        for table in tables.values():
            row = table[index]["List"]
            actual = (int(row[2]), int(row[3]), int(row[4]), float(row[10]))
            assert actual == expected, (name, actual, expected)
        match = re.search(r'\["' + name + r'"\] = new\((\d+), (\d+), (\d+), ([.\d]+)f\)', source)
        assert match, name
        assert (int(match[1]), int(match[2]), int(match[3]), float(match[4])) == TUNED.get(name, expected), name

    controller_bundle = UnityPy.load(str(ROOT / "lib/ani/mot/1/000_00.unity3d"))
    controller = next(o.read() for o in controller_bundle.objects if o.type.name == "AnimatorController")
    names = {value for _, value in controller.m_TOS}
    motion_source = (SOURCE / "GameAdapter/MotionCatalog.cs").read_text(encoding="utf-8-sig")
    states = {int(number): state for number, state in re.findall(r'\[(\d+)\] = "([^"]+)"', motion_source)}
    assert set(CONFIG["Motions"].values()) == set(states)
    assert all(state in names for state in states.values())
    assert all(f"Base Layer.Idle.chara.Pose_D_{personality:02}_Loop" in names for personality in range(12))
    print(json.dumps({"ok": True, "expressions": len(EXPRESSION_ROWS),
                      "personalities": len(tables), "motions": len(states)}))


if __name__ == "__main__":
    main()
