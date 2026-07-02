"""End-to-end coverage for app_answer_dialog (test automation).

Skipped (whole class) when the `answer-dialog` flag is off — it defaults off in
shipping builds; set `answer-dialog = true` under `[features]` in the .celbridge.

Coverage boundary: Confirmation and InputText are exercised here because
`explorer.delete(showDialog=True)` and `explorer.rename` reliably surface
those dialogs through MCP. Alert and ResourcePicker have no clean MCP
trigger (Alert is reached only via error paths like rename-on-readonly;
ResourcePicker fires only from JS contribution `PickFile`/`PickImage`
calls), so their schedule-to-broadcast contract is covered by C# unit
tests in DialogServiceAnswerTests rather than here.
"""
import pytest

from celbridge.cel_proxy import CelError

from .helpers import delete_if_exists


# Dialog kind identifiers — match Celbridge.Dialog.DialogKind enum names.
CONFIRMATION = "Confirmation"
INPUT_TEXT = "InputText"


@pytest.fixture(autouse=True)
def workspace(answer_dialog_available, explorer):
    delete_if_exists(explorer, "TestAnswerDialog")
    explorer.create_folder("TestAnswerDialog")
    yield
    delete_if_exists(explorer, "TestAnswerDialog")


class TestAnswerDialog:

    def test_confirmation_dialog_is_answered(self, app, explorer, file):
        explorer.create_file("TestAnswerDialog/to_delete.txt")

        outcome = app.answer_dialog(CONFIRMATION)
        assert outcome == "ok"

        explorer.delete("TestAnswerDialog/to_delete.txt", showDialog=True)

        items = file.list_contents("TestAnswerDialog")
        names = [i["name"] for i in items]
        assert "to_delete.txt" not in names

    def test_input_text_dialog_receives_payload(self, app, explorer, file):
        explorer.create_file("TestAnswerDialog/before.txt")

        outcome = app.answer_dialog(INPUT_TEXT, "after.txt")
        assert outcome == "ok"

        explorer.rename("TestAnswerDialog/before.txt")

        items = file.list_contents("TestAnswerDialog")
        names = [i["name"] for i in items]
        assert "after.txt" in names
        assert "before.txt" not in names

    def test_re_schedule_overwrites(self, app, explorer, file):
        explorer.create_file("TestAnswerDialog/before.txt")

        app.answer_dialog(INPUT_TEXT, "first.txt")
        app.answer_dialog(INPUT_TEXT, "second.txt")

        explorer.rename("TestAnswerDialog/before.txt")

        items = file.list_contents("TestAnswerDialog")
        names = [i["name"] for i in items]
        assert "second.txt" in names
        assert "first.txt" not in names

    def test_invalid_dialog_kind_raises(self, app):
        # The tool validates dialogKind against the DialogKind enum and returns
        # an error for an unknown value. This is the tool-level failure path
        # reachable while the feature is on; the flag-off path is gated out by
        # the autouse fixture here and covered by C# unit tests instead.
        with pytest.raises(CelError):
            app.answer_dialog("NotARealDialogKind")
