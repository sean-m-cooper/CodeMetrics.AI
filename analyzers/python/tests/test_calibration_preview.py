from copy import deepcopy
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import source_facts
from calibration_preview import candidate, membership_discounts

FIXTURE = Path(__file__).resolve().parents[3] / "shared/calibration/fixtures/python/tests/complexity-patterns.py"
EXPECTED = {
    "straight": (1, 1), "comprehension": (2, 2), "expanded_loop": (2, 2),
    "filtered_comprehension": (3, 3), "expanded_filter": (3, 3),
    "nested_comprehension": (4, 4), "expanded_nested_filter": (4, 4),
    "chained_range": (2, 2), "boolean_range": (2, 2),
    "grouped_handler": (2, 2), "separate_handlers": (3, 3), "cleanup": (1, 1), "sequential_awaits": (1, 1),
    "match_membership": (6, 2), "if_membership": (2, 2),
    "class_membership": (4, 2), "isinstance_membership": (2, 2),
    "guarded_membership": (6, 4), "expanded_guard": (4, 4),
    "wildcard_guard": (2, 2), "wildcard_default": (1, 1),
    "structural_alternatives": (3, 3), "nested_pattern_alternatives": (3, 3),
    "as_bound_membership": (3, 2), "separate_cases": (4, 4), "separate_ifs": (4, 4),
    "guard_comprehension": (5, 5), "outer": (1, 1), "outer.inner": (3, 2),
    "singleton_membership": (4, 2), "class_structural_alternatives": (3, 3), "membership_with_body": (4, 3),
}


class CalibrationTests(unittest.TestCase):
    def setUp(self):
        self.source = FIXTURE.read_text(encoding="utf-8")
        self.facts = source_facts.analyze(FIXTURE)
        self.discounts = membership_discounts(self.source, FIXTURE.name)
        # Independent, manually specified v1 values preserve the original
        # calibration experiment without loading historical production code.
        self.legacy = deepcopy(self.facts)
        self.legacy["measurementPolicy"] = "python/owned-function-complexity-v1"
        for function in self.legacy["functions"]:
            before, after = EXPECTED[function["member"]]
            function["complexity"]["value"] = before
            function["complexity"]["decisions"]["matchAlternatives"] += before - after

    def test_independently_specified_current_and_candidate_counts(self):
        # Compile verifies pattern restrictions without executing any fixture body.
        compile(self.source, str(FIXTURE), "exec")
        proposed, changes = candidate(self.legacy, "membership", self.discounts)
        current = {f["member"]: f["complexity"]["value"] for f in self.facts["functions"]}
        preview = {f["member"]: f["complexity"]["value"] for f in proposed["functions"]}
        self.assertEqual(set(current), set(EXPECTED))
        for name, (before, after) in EXPECTED.items():
            with self.subTest(name=name):
                self.assertEqual(current[name], after)
                self.assertEqual(preview[name], after)
        self.assertEqual(len(changes), 7)

    def test_candidates_do_not_mutate_production_facts_or_suppress_guard_and_body_logic(self):
        original = deepcopy(self.legacy)
        for mode in ["membership", "without-except", "without-comprehensions"]:
            result, _ = candidate(self.legacy, mode, self.discounts)
            for function in result["functions"]:
                self.assertEqual(function["complexity"]["value"], 1 + sum(function["complexity"]["decisions"].values()))
        self.assertEqual(self.legacy, original)
        with self.assertRaises(ValueError):
            candidate(self.facts, "unknown")
        with self.assertRaises(ValueError):
            candidate(self.legacy, "membership", {self.legacy["functions"][0]["id"]: 99})
        with self.assertRaises(ValueError):
            candidate(self.facts, "membership", self.discounts)

    def test_nested_structural_alternatives_and_capture_paths_remain_visible(self):
        by_name = {f["member"]: f for f in self.facts["functions"]}
        for name in ["structural_alternatives", "nested_pattern_alternatives", "class_structural_alternatives"]:
            self.assertEqual(by_name[name]["complexity"]["decisions"]["matchAlternatives"], 1)
        self.assertEqual(by_name["guarded_membership"]["complexity"]["decisions"]["matchGuards"], 1)
        self.assertEqual(by_name["guarded_membership"]["complexity"]["decisions"]["booleanOperators"], 1)
        self.assertEqual(by_name["outer"]["complexity"]["value"], 1)


if __name__ == "__main__":
    unittest.main()
