import unittest

import analyze


class FrontendCsvPathTests(unittest.TestCase):
    def test_accepts_filename_without_host_path(self):
        analyze.validate_frontend_csv_path("frontend.csv", analyze.DOCS / "frontend.csv")

    def test_existing_absolute_and_repo_relative_paths_still_validate(self):
        expected = analyze.DOCS / "frontend.csv"
        analyze.validate_frontend_csv_path(str(expected), expected)
        analyze.validate_frontend_csv_path("docs/space-battle/frontend.csv", expected)

    def test_rejects_wrong_csv_file(self):
        expected = analyze.DOCS / "frontend.csv"
        for path in ("other.csv", "docs/space-battle/other.csv", str(analyze.DOCS / "other.csv")):
            with self.subTest(path=path), self.assertRaises(ValueError):
                analyze.validate_frontend_csv_path(path, expected)


if __name__ == "__main__":
    unittest.main()
