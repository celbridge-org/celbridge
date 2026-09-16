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
def workshop():
    return celbridge.workshop


@pytest.fixture(scope="session")
def webview():
    return celbridge.webview


@pytest.fixture(scope="session")
def spreadsheet():
    return celbridge.spreadsheet


@pytest.fixture(scope="session")
def data():
    return celbridge.data


@pytest.fixture(scope="class")
def eval_enabled(app):
    """True when the webview-dev-tools-eval flag is on.

    Only webview.eval is gated by the flag; the tools that route through the in-page
    shim are not. Cases that need to inject script skip themselves when it is off.
    """
    flags = app.get_state().get("featureFlags", {})
    return flags.get("webview-dev-tools-eval", False)


@pytest.fixture(scope="session")
def answer_dialog_available(app):
    """Skip the suite (or a single test) when app_answer_dialog is unavailable.

    The tool answers dialogs only in a Debug build of Celbridge and refuses every
    call in a Release build, so the fixture skips unless `app_get_state` reports
    the Debug configuration.
    """
    state = app.get_state()
    if state.get("configuration") != "Debug":
        pytest.skip("app_answer_dialog is available in debug builds only")
    return True
