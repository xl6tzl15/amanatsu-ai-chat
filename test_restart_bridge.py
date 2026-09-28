import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

import psutil
import restart_bridge


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = (ROOT / "ModSource/AiChat/bridge.py").resolve()
CONFIG = (ROOT / "ModSource/AiChat/bridge.local.json").resolve()


class RestartBridgeTests(unittest.TestCase):
    def test_exact_process_match_only(self):
        process = MagicMock()
        process.info = {"cmdline": ["python", "-u", "ModSource/AiChat/bridge.py",
                                    "--config", "ModSource/AiChat/bridge.local.json", "--port", "38429"]}
        process.cwd.return_value = str(ROOT)
        process.net_connections.return_value = [SimpleNamespace(
            status=psutil.CONN_LISTEN,
            laddr=SimpleNamespace(ip="127.0.0.1", port=38429))]
        with patch("restart_bridge.psutil.process_iter", return_value=[process]):
            self.assertEqual(restart_bridge.matching_bridges(SCRIPT, CONFIG, 38429), [process])
            self.assertEqual(restart_bridge.matching_bridges(SCRIPT, CONFIG, 38430), [])

    def test_occupied_unmanaged_port_is_not_stopped(self):
        connection = MagicMock()
        connection.__enter__.return_value.connect_ex.return_value = 0
        with patch("restart_bridge.matching_bridges", return_value=[]), \
             patch("restart_bridge.socket.socket", return_value=connection), \
             patch("restart_bridge.subprocess.Popen") as launch:
            with self.assertRaisesRegex(RuntimeError, "unmanaged process"):
                restart_bridge.restart(ROOT, CONFIG, 38429)
            launch.assert_not_called()


if __name__ == "__main__":
    unittest.main()
