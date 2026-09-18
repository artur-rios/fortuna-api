"""Unit tests for the pure helpers in check_blank_lines.py.

Run from the repository root:  python -m unittest discover -s scripts -p "test_*.py"
"""

import tempfile
import unittest
from pathlib import Path

from check_blank_lines import discover_files, find_violations, is_checked


def code(*lines):
    return "\n".join(lines) + "\n"


class FindViolationsTests(unittest.TestCase):
    def test_given_return_directly_after_a_statement_when_checked_then_it_is_reported(self):
        text = code(
            "{",
            "    var total = 1;",
            "    return total;",
            "}",
        )

        self.assertEqual([3], find_violations(text))

    def test_given_blank_line_above_return_when_checked_then_nothing_is_reported(self):
        text = code(
            "{",
            "    var total = 1;",
            "",
            "    return total;",
            "}",
        )

        self.assertEqual([], find_violations(text))

    def test_given_return_first_in_block_when_checked_then_nothing_is_reported(self):
        text = code(
            "if (value is null)",
            "{",
            "    return;",
            "}",
        )

        self.assertEqual([], find_violations(text))

    def test_given_break_and_yield_after_statements_when_checked_then_each_is_reported(self):
        text = code(
            "{",
            "    Call();",
            "    break;",
            "    Call();",
            "    yield return item;",
            "    Call();",
            "    yield break;",
            "}",
        )

        self.assertEqual([3, 5, 7], find_violations(text))

    def test_given_statement_after_case_or_default_label_when_checked_then_nothing_is_reported(self):
        text = code(
            "switch (kind)",
            "{",
            "    case Kind.First:",
            "        return 1;",
            "    case Kind.Second when flag:",
            "        break;",
            "    default:",
            "        return 0;",
            "}",
        )

        self.assertEqual([], find_violations(text))

    def test_given_braceless_control_bodies_when_checked_then_nothing_is_reported(self):
        text = code(
            "{",
            "    Call();",
            "    if (value is null)",
            "        return;",
            "    else",
            "        return;",
            "    foreach (var item in items)",
            "        yield return item;",
            "    if (first &&",
            "        second)",
            "        break;",
            "}",
        )

        self.assertEqual([], find_violations(text))

    def test_given_comment_attached_without_blank_above_when_checked_then_statement_is_reported(self):
        text = code(
            "{",
            "    Call();",
            "    // Why the early exit is safe.",
            "    return;",
            "}",
        )

        self.assertEqual([4], find_violations(text))

    def test_given_blank_line_above_attached_comment_when_checked_then_nothing_is_reported(self):
        text = code(
            "{",
            "    Call();",
            "",
            "    // Why the early exit is safe.",
            "    // Second line of the explanation.",
            "    return;",
            "}",
        )

        self.assertEqual([], find_violations(text))

    def test_given_trailing_comment_on_spaced_statement_when_checked_then_nothing_is_reported(self):
        text = code(
            "{",
            "    Call(); // trailing comments do not count as a block",
            "",
            "    return;",
            "}",
        )

        self.assertEqual([], find_violations(text))

    def test_given_statement_after_multi_line_call_when_checked_then_it_is_reported(self):
        text = code(
            "{",
            "    Call(",
            "        argument);",
            "    return;",
            "}",
        )

        self.assertEqual([4], find_violations(text))

    def test_given_return_keyword_inside_raw_string_when_checked_then_it_is_ignored(self):
        text = code(
            "{",
            '    var sql = """',
            "        select 1;",
            "        return 2;",
            "        break;",
            '        """;',
            "",
            "    return sql;",
            "}",
        )

        self.assertEqual([], find_violations(text))

    def test_given_return_opening_a_raw_string_when_checked_then_the_return_itself_is_checked(self):
        text = code(
            "{",
            "    var filter = Build();",
            '    return $"""',
            "        select {filter};",
            "        return 1;",
            '        """;',
            "}",
        )

        self.assertEqual([3], find_violations(text))

    def test_given_statement_after_a_closed_raw_string_when_checked_then_it_is_reported(self):
        text = code(
            "{",
            '    var sql = """',
            "        select 1;",
            '        """;',
            "    return sql;",
            "}",
        )

        self.assertEqual([5], find_violations(text))

    def test_given_single_line_raw_string_when_checked_then_state_is_unchanged(self):
        text = code(
            "{",
            '    var json = """{"a":1}""";',
            "    return json;",
            "}",
        )

        self.assertEqual([3], find_violations(text))

    def test_given_identifier_starting_with_return_when_checked_then_it_is_not_a_statement(self):
        text = code(
            "{",
            "    Call();",
            "    returnValue = 1;",
            "    breakpoint = 2;",
            "}",
        )

        self.assertEqual([], find_violations(text))


class FileSelectionTests(unittest.TestCase):
    def test_given_migrations_and_build_output_when_selecting_then_they_are_skipped(self):
        self.assertFalse(is_checked(Path("src/Data/Migrations/20260101_Initial.cs")))
        self.assertFalse(is_checked(Path("src/Api/bin/Debug/Generated.cs")))
        self.assertFalse(is_checked(Path("src/Api/obj/Debug/Generated.cs")))
        self.assertFalse(is_checked(Path("src/Api/Program.fs")))
        self.assertTrue(is_checked(Path("src/Data.Sqlite.Migrations/DesignTimeFactory.cs")))
        self.assertTrue(is_checked(Path("tests/Api.Tests/ProgramTests.cs")))

    def test_given_repository_layout_when_discovering_then_only_src_and_tests_are_searched(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for relative in [
                "src/Api/Program.cs",
                "src/Data/Migrations/Initial.cs",
                "tests/Api.Tests/ProgramTests.cs",
                "docs/Sample.cs",
            ]:
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("", encoding="utf-8")

            found = [path.relative_to(root).as_posix() for path in discover_files(root)]

        self.assertEqual(["src/Api/Program.cs", "tests/Api.Tests/ProgramTests.cs"], found)


if __name__ == "__main__":
    unittest.main()
