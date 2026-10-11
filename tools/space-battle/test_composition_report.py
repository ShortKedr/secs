"""Focused rejection tests against the frozen diagnostic artifacts."""

from copy import deepcopy
import unittest
from unittest.mock import patch

import analyze
import composition_report


class CompositionValidationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.runs = {(p, f): analyze.read_json(analyze.RESULTS / f"{p}-{f}.json")
                    for p in analyze.PROFILES for f in analyze.FRAMEWORKS}

    def rejected(self, mutation, expected):
        def read(path):
            data = analyze.read_json(path)
            if path.name == "low.json":
                data = deepcopy(data)
                mutation(data)
            return data
        with patch.object(composition_report, "composition_charts", side_effect=AssertionError("Invalid data reached rendering")):
            with self.assertRaisesRegex(ValueError, expected):
                composition_report.composition_section(analyze.DOCS, self.runs, read, analyze.require, analyze.table)

    def test_wrong_duration(self):
        self.rejected(lambda d: d.update(Ticks=1), "duration mismatch")

    def test_stale_core(self):
        self.rejected(lambda d: d["BinaryHashes"].update({"SpaceBattle.Core.dll": "0" * 64}), "measured DLL mismatch")

    def test_unverified_checkpoint(self):
        self.rejected(lambda d: d["Checkpoints"][0].update(NativeComponentCountsVerified=False), "native census/checkpoints mismatch")

    def test_wrong_mean(self):
        self.rejected(lambda d: d["Summary"].update(MeanComponentsPerEntity=1), "aggregate mismatch: MeanComponentsPerEntity")

    def test_corrupt_component_additions(self):
        self.rejected(lambda d: d["Components"][0].update(AddsDuringSimulation=0), "per-type structural conservation mismatch")


if __name__ == "__main__":
    unittest.main()
