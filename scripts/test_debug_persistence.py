"""CPU reference checks for the debug-only peak-retention policy."""
import math
import unittest
from pathlib import Path


def retain(old, live, seconds, half_life, paused=False):
    if paused:
        return old
    decayed = tuple(x * 0.5 ** (seconds / half_life) for x in old)
    return live if math.hypot(*live) >= math.hypot(*decayed) else decayed


class PersistenceTests(unittest.TestCase):
    def test_draw_does_not_unconditionally_destroy_history(self):
        source = (Path(__file__).resolve().parents[1] / 'ClientPlugin/ShaderFramework/VelocityDebugPass.cs').read_text(encoding='utf-8-sig')
        draw = source.split('public static void Draw(')[1].split('public static void Release()')[0]
        self.assertNotIn('lock (Gate)\n        {\n            ResetPersistence();', draw)
        release = source.split('public static void Release()')[1].split('static bool OverlayOn()')[0]
        self.assertIn('ResetPersistence();', release)

    def test_half_life(self):
        self.assertEqual(retain((4, 0), (0, 0), 2, 2), (2, 0))

    def test_pause_ignores_zeroed_live(self):
        self.assertEqual(retain((4, -2), (0, 0), 100, 2, True), (4, -2))

    def test_opposite_direction_does_not_cancel(self):
        self.assertEqual(retain((4, 0), (-1, 0), 0, 2), (4, 0))

    def test_frame_rate_independence(self):
        v = (4, 0)
        for _ in range(120):
            v = retain(v, (0, 0), 1 / 60, 2)
        self.assertAlmostEqual(v[0], 2)


if __name__ == '__main__':
    unittest.main()
