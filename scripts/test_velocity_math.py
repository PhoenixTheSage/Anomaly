"""Numerical regression oracles for the shader's row-vector reconstruction."""
import math
import random
import unittest


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def mul(v, m):
    return tuple(dot(v, column) for column in zip(*m))


def recover(world, translation, rows, corrected=True):
    cofactors = [cross(rows[1], rows[2]), cross(rows[2], rows[0]), cross(rows[0], rows[1])]
    determinant = dot(rows[0], cofactors[0])
    inverse = list(zip(*cofactors)) if corrected else cofactors
    divisor = determinant if corrected else abs(determinant)
    return mul(tuple(x-y for x, y in zip(world, translation)),
               [[v/divisor for v in row] for row in inverse])


class VelocityMath(unittest.TestCase):
    def test_rotated_scaled_mirrored_instances(self):
        rng = random.Random(619)
        old_failures = 0
        for _ in range(500):
            angle = rng.uniform(-math.pi, math.pi)
            c, s = math.cos(angle), math.sin(angle)
            scale = [rng.uniform(.3, 3) * rng.choice((-1, 1)) for _ in range(3)]
            rows = [[c*scale[0], s*scale[0], 0], [-s*scale[1], c*scale[1], 0], [0, 0, scale[2]]]
            point = [rng.uniform(-30, 30) for _ in range(3)]
            translation = [rng.uniform(-1000, 1000) for _ in range(3)]
            world = [x+y for x, y in zip(mul(point, rows), translation)]
            self.assertLess(max(abs(a-b) for a, b in zip(point, recover(world, translation, rows))), 1e-9)
            old_failures += max(abs(a-b) for a, b in zip(point, recover(world, translation, rows, False))) > 1e-4
        self.assertGreater(old_failures, 450)

    def test_camera_translation_pixel_delta(self):
        # Static point at z=-10; current camera moves +1 X. Width=1920, focal=1.
        previous = (0, 0, -10)
        current = (-1, 0, -10)
        delta = (1, 0, 0)
        recovered = tuple(a+b for a, b in zip(current, delta))
        self.assertEqual(recovered, previous)
        pixel_x = lambda p: (p[0]/-p[2]*.5+.5)*1920
        self.assertAlmostEqual(pixel_x(recovered)-pixel_x(current), 96)
        self.assertEqual(pixel_x(current)-pixel_x(current), 0)  # old missing-origin result

    def test_previous_object_transform_already_has_previous_origin(self):
        current_camera, previous_camera = 1001, 1000
        current_object, previous_object = 1020, 1018
        current_relative = current_object-current_camera
        previous_relative = previous_object-previous_camera
        self.assertEqual(current_relative, 19)
        self.assertEqual(previous_relative, 18)
        # Adding camera delta after an explicit previous-world transform double-counts it.
        self.assertNotEqual(previous_relative + current_camera-previous_camera, previous_relative)

    def test_stationary_rotated_instance_is_zero(self):
        rows = [[0, 1, 0], [-1, 0, 0], [0, 0, 1]]
        point = (3, 7, -10)
        world = mul(point, rows)
        previous = mul(recover(world, (0, 0, 0), rows), rows)
        self.assertEqual(previous, world)

    def test_backward_reprojection_both_axes(self):
        previous, current = (100, 200), (103, 204)
        motion = tuple(p-c for p, c in zip(previous, current))
        self.assertEqual(motion, (-3, -4))
        self.assertEqual(tuple(c+v for c, v in zip(current, motion)), previous)

    def test_shader_direction_matches_contract(self):
        from pathlib import Path
        root = Path(__file__).resolve().parents[1]
        for shader in ('Anomaly.hlsli', 'CameraVelocity.hlsl'):
            source = (root / 'Assets/Shaders' / shader).read_text(encoding='utf-8-sig')
            self.assertIn('(prevUv - currUv)', source)
            self.assertNotIn('(currUv - prevUv)', source)


if __name__ == '__main__':
    unittest.main()
