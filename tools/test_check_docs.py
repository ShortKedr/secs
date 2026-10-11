import unittest
from types import SimpleNamespace
from unittest.mock import patch

import check_docs


class MarkdownPortabilityTests(unittest.TestCase):
    def test_nested_image_link(self):
        self.assertEqual(list(check_docs.inline_links("[![Battle](action.png)](REPORT.md)")), ["action.png", "REPORT.md"])

    def test_parentheses_destination(self):
        self.assertEqual(list(check_docs.inline_links("[file](notes/example(1).md)")), ["notes/example(1).md"])

    def test_fenced_examples_are_not_links(self):
        clean, code = check_docs.without_fences("[Real](README.md)\n```powershell\n[x](C:/example.md)\n```\n")
        self.assertEqual(list(check_docs.inline_links(clean)), ["README.md"])
        self.assertEqual(code, ["powershell\n[x](C:/example.md)"])

    def test_unclosed_fence(self):
        with self.assertRaisesRegex(ValueError, "Unclosed"):
            check_docs.without_fences("```text\nexample")

    def test_github_heading_anchors(self):
        result = check_docs.anchors("## Tick Timing\n## Tick Timing\n## Системы и компоненты\n## `Get<T>` + Queries\n")
        self.assertEqual(result, {"tick-timing", "tick-timing-1", "системы-и-компоненты", "gett--queries"})

    def test_thousands_formatting_is_not_data_change(self):
        self.assertEqual(check_docs.table_numbers("| Metric | 3,600 | 1,554 | 4.1131 |"),
                         check_docs.table_numbers("| Метрика | 3600 | 1554 | 4.1131 |"))

    def test_path_case_is_checked(self):
        with self.assertRaisesRegex(ValueError, "Wrong path case"):
            check_docs.exact_case(check_docs.ROOT / "readme.md")

    def test_git_stdin_has_literal_lf(self):
        with patch("check_docs.subprocess.run", return_value=SimpleNamespace(returncode=1, stdout=b"", stderr=b"")) as run:
            check_docs.check()
        payload = run.call_args.kwargs["input"]
        self.assertIsInstance(payload, bytes)
        self.assertNotIn(b"\r", payload)
        self.assertTrue(payload.endswith(b"\n"))


if __name__ == "__main__":
    unittest.main()
