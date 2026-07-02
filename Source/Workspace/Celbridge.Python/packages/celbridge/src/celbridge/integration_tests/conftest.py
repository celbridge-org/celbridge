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


@pytest.fixture(scope="session")
def data():
    return celbridge.data


@pytest.fixture(scope="session")
def answer_dialog_available(app):
    """Skip the suite (or a single test) when the dialog-answer surface is unavailable.

    The tool ships in every build configuration and is gated by the `answer-dialog`
    flag, which defaults off in shipping builds. When the flag is off,
    `app_get_state.featureFlags` reports it disabled and we skip. Enable it with
    `answer-dialog = true` under `[features]` in the project .celbridge.
    """
    state = app.get_state()
    feature_flags = state.get("featureFlags", {})
    if not feature_flags.get("answer-dialog", False):
        pytest.skip(
            "Dialog answer feature not enabled — set answer-dialog = true "
            "under [features] in the project .celbridge"
        )
    return True
