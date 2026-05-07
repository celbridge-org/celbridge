"""Celbridge MCP integration test suite.

Launched from the REPL via cel.test([class_filter]); see celbridge.cel_proxy.

Pytest does collection, fixtures, and assertion rewriting under the hood,
but its terminal reporter is disabled and its output capture is turned off
so it leaves the Windows console untouched. CelbridgeReporter below provides
the [n/N] PASS/FAIL/SKIP/ERROR output cel.test() users expect, mirrors
results into the Celbridge log, and implements the class-substring filter.
"""
from collections import Counter
from pathlib import Path

import pytest

import celbridge


GREEN = "\033[92m"
RED = "\033[91m"
YELLOW = "\033[93m"
RESET = "\033[0m"


class CelbridgeReporter:

    def __init__(self, class_filter=None):
        if class_filter is None:
            self._filters = None
        elif isinstance(class_filter, str):
            self._filters = [class_filter.lower()]
        else:
            self._filters = [name.lower() for name in class_filter]
        self._total = 0
        self._current = 0
        self._failures = []
        self._errors = []
        self._available_classes = []

    def pytest_collection_modifyitems(self, items):
        seen = set()
        for item in items:
            cls_name = item.parent.name if item.parent else None
            if cls_name and cls_name not in seen:
                seen.add(cls_name)
                self._available_classes.append(cls_name)

        if self._filters is not None:
            items[:] = [
                item for item in items
                if item.parent
                and any(needle in item.parent.name.lower() for needle in self._filters)
            ]

        self._total = len(items)

    def pytest_collection_finish(self, session):
        if self._total == 0:
            if self._filters is not None:
                names = ", ".join(self._available_classes)
                shown = self._filters[0] if len(self._filters) == 1 else self._filters
                print(f"\nNo test classes match {shown!r}. Available: {names}\n")
            else:
                print("\nNo tests collected.\n")
            return
        by_class = Counter()
        for item in session.items:
            if item.parent:
                by_class[item.parent.name] += 1
        if self._filters is not None:
            running = ", ".join(sorted(by_class))
            shown = self._filters[0] if len(self._filters) == 1 else self._filters
            print(f"\nFilter: {shown!r} -> {running}")
        print(f"Running {self._total} tests across {len(by_class)} classes...\n")

    def pytest_runtest_logreport(self, report):
        if report.when == "setup" and report.skipped:
            self._current += 1
            reason = self._skip_reason(report)
            self._emit(report.nodeid, f"SKIP -- {reason}", YELLOW, celbridge.app.log_warning)
        elif report.when == "setup" and report.failed:
            self._current += 1
            self._emit(report.nodeid, "ERROR", RED, celbridge.app.log_error)
            self._errors.append((report.nodeid, str(report.longrepr)))
        elif report.when == "call":
            self._current += 1
            if report.passed:
                self._emit(report.nodeid, "PASS", GREEN, celbridge.app.log)
            elif report.failed:
                self._emit(report.nodeid, "FAIL", RED, celbridge.app.log_error)
                self._failures.append((report.nodeid, str(report.longrepr)))

    def pytest_sessionfinish(self, session, exitstatus):
        if self._total == 0:
            return
        passed = self._current - len(self._failures) - len(self._errors)
        print()
        if self._failures or self._errors:
            print(
                f"Results: {GREEN}{passed} passed{RESET}, "
                f"{RED}{len(self._failures)} failed{RESET}, "
                f"{RED}{len(self._errors)} errors{RESET}"
            )
        else:
            print(f"Results: {GREEN}{passed} passed{RESET}, 0 failed, 0 errors")

        if self._failures:
            print(f"\n{RED}Failures:{RESET}")
            for nodeid, repr_ in self._failures:
                print(f"  {RED}{self._short_nodeid(nodeid)}{RESET}")
                _print_failure_detail(repr_)

        if self._errors:
            print(f"\n{RED}Errors:{RESET}")
            for nodeid, repr_ in self._errors:
                print(f"  {RED}{self._short_nodeid(nodeid)}{RESET}")
                _print_failure_detail(repr_)

    def pytest_internalerror(self, excrepr, excinfo):
        print(f"\n{RED}Pytest internal error:{RESET}")
        print(str(excrepr))

    def _emit(self, nodeid, label, colour, logger):
        prefix = f"[{self._current}/{self._total}]"
        short = self._short_nodeid(nodeid)
        print(f"  {prefix} {colour}{label}{RESET}: {short}")
        logger(f"  {prefix} {label}: {short}")

    @staticmethod
    def _short_nodeid(nodeid):
        # "test_app.py::TestApp::test_get_status" -> "TestApp.get_status".
        # The "test_" prefix is required by pytest discovery but redundant
        # in output, where [N/M] PASS and the TestX class already signal
        # this is a test result.
        parts = nodeid.split("::")
        method = parts[-1]
        if method.startswith("test_"):
            method = method[len("test_"):]
        if len(parts) >= 3:
            return f"{parts[-2]}.{method}"
        if len(parts) == 2:
            return method
        return nodeid

    @staticmethod
    def _skip_reason(report):
        # report.longrepr for skips is (filename, lineno, reason).
        if isinstance(report.longrepr, tuple) and len(report.longrepr) == 3:
            return report.longrepr[2]
        return "skipped"


def _print_failure_detail(traceback_str):
    # The full traceback is verbose; the AssertionError block (and any
    # diff that pytest writes for dict/list/string mismatches) is the
    # informative part. Print every line from the first AssertionError
    # to the end so we do not truncate diagnostic context.
    lines = traceback_str.rstrip().splitlines()
    start_index = 0
    for index, line in enumerate(lines):
        if "AssertionError" in line:
            start_index = index
            break
    for line in lines[start_index:]:
        print(f"    {line}")


def run_suite(class_filter=None):
    """Run the integration suite.

    class_filter is a case-insensitive substring match against test class
    names (e.g. "Spreadsheet"), or an iterable of such substrings. When
    omitted, every test class runs.
    """
    suite_path = Path(__file__).parent
    args = [
        str(suite_path),
        # Disable pytest's terminal reporter so it produces no output of
        # its own; CelbridgeReporter handles every visible line. The
        # --color flag is registered by this plugin, so we cannot pass it
        # alongside no:terminal — but with the reporter disabled, pytest
        # has no path that loads colorama anyway.
        "-p", "no:terminal",
        # No output capture. The default fd-level capture redirects file
        # descriptors and leaves the Windows console in a state IPython's
        # prompt_toolkit cannot recover from once pytest.main() returns.
        "--capture=no",
    ]
    return pytest.main(args, plugins=[CelbridgeReporter(class_filter)])
