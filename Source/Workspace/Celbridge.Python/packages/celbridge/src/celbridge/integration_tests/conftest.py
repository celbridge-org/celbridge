"""Session-scoped fixtures wrapping the celbridge proxy modules.

These replace the module-level globals used by the previous test_suite.py.
"""
import pytest

import celbridge


@pytest.fixture(scope="session")
def app():
    return celbridge.app


@pytest.fixture(scope="session")
def file():
    return celbridge.file


@pytest.fixture(scope="session")
def guides():
    return celbridge.guides


@pytest.fixture(scope="session")
def explorer():
    return celbridge.explorer


@pytest.fixture(scope="session")
def document():
    return celbridge.document


@pytest.fixture(scope="session")
def package():
    return celbridge.package


@pytest.fixture(scope="session")
def webview():
    return celbridge.webview


@pytest.fixture(scope="session")
def spreadsheet():
    return celbridge.spreadsheet
