import json

import pytest

from celbridge.cel_proxy import CelError

from .helpers import close_if_open, delete_if_exists


@pytest.fixture(autouse=True)
def workspace(explorer, document):
    delete_if_exists(explorer, "TestExplorer")
    explorer.create_folder("TestExplorer")
    yield
    close_if_open(document, "TestExplorer/hello.txt")
    close_if_open(document, "TestExplorer/original.txt")
    close_if_open(document, "TestExplorer/moved.txt")
    delete_if_exists(explorer, "TestExplorer")


class TestExplorer:

    def test_create_file(self, explorer, file):
        explorer.create_file("TestExplorer/hello.txt")
        items = file.list_contents("TestExplorer")
        names = [i["name"] for i in items]
        assert "hello.txt" in names

    def test_create_folder(self, explorer, file):
        explorer.create_folder("TestExplorer/subfolder")
        items = file.list_contents("TestExplorer")
        names = [i["name"] for i in items]
        assert "subfolder" in names

    def test_select(self, explorer):
        explorer.create_file("TestExplorer/hello.txt")
        explorer.select("TestExplorer/hello.txt")

    def test_get_state(self, explorer):
        explorer.create_file("TestExplorer/hello.txt")
        explorer.select("TestExplorer/hello.txt")
        ctx = explorer.get_state()
        assert "selectedResource" in ctx
        assert "expandedFolders" in ctx

    def test_expand_and_collapse_folder(self, explorer):
        explorer.expand_folder("TestExplorer", expanded=True)
        explorer.expand_folder("TestExplorer", expanded=False)

    def test_collapse_all(self, explorer):
        explorer.expand_folder("TestExplorer", expanded=True)
        explorer.collapse_all()

    def test_copy(self, explorer, file):
        explorer.create_file("TestExplorer/hello.txt")
        explorer.copy("TestExplorer/hello.txt", "TestExplorer/hello_copy.txt")
        items = file.list_contents("TestExplorer")
        names = [i["name"] for i in items]
        assert "hello_copy.txt" in names

    def test_move(self, explorer, file):
        explorer.create_file("TestExplorer/original.txt")
        explorer.move("TestExplorer/original.txt", "TestExplorer/moved.txt")
        items = file.list_contents("TestExplorer")
        names = [i["name"] for i in items]
        assert "moved.txt" in names
        assert "original.txt" not in names

    def test_move_preserves_referential_integrity(self, explorer, file, data):
        # The reference-rewrite cascade in IResourceFileSystem.MoveAsync must
        # leave no broken project: references after a move. The referencer is
        # .json (an allowlisted scanner extension) so the cascade actually
        # walks it; .md would be invisible to the scanner.
        file.write(
            "TestExplorer/source.json",
            "{\"target\": \"project:TestExplorer/target.md\"}",
        )
        file.write("TestExplorer/target.md", "Target body.\n")

        explorer.move("TestExplorer/target.md", "TestExplorer/renamed.md")

        # No project: reference in our test folder should be broken after the move.
        report = data.check_project()
        broken = [
            entry for entry in report.get("brokenReferences", [])
            if entry["source"].startswith("project:TestExplorer/")
                or entry["missingTarget"].startswith("project:TestExplorer/")
        ]
        assert broken == [], f"Move broke references: {broken}"

    def test_delete_with_break_references_leaves_dangling_reference(self, explorer, file, data):
        # Deleting a referenced resource under break_references should leave
        # the reference dangling, surfaced by data_check_project.
        file.write(
            "TestExplorer/has_ref.json",
            "{\"target\": \"project:TestExplorer/will_delete.md\"}",
        )
        file.write("TestExplorer/will_delete.md", "Doomed.\n")

        explorer.delete(
            "TestExplorer/will_delete.md",
            reference_policy="break_references",
        )

        report = data.check_project()
        broken_targets = {
            entry["missingTarget"]
            for entry in report.get("brokenReferences", [])
        }
        assert "project:TestExplorer/will_delete.md" in broken_targets

    def test_undo_redo(self, explorer):
        explorer.create_file("TestExplorer/undo_test.txt")
        explorer.undo()
        explorer.redo()

    def test_delete(self, explorer, file):
        explorer.create_file("TestExplorer/to_delete.txt")
        explorer.delete("TestExplorer/to_delete.txt")
        items = file.list_contents("TestExplorer")
        names = [i["name"] for i in items]
        assert "to_delete.txt" not in names

    # NOTE: explorer.rename and explorer.duplicate are interactive (show dialogs)
    # and cannot be tested in an automated script.

    def test_create_file_invalid_resource_key(self, explorer):
        with pytest.raises(CelError):
            explorer.create_file("\\backslash\\not\\allowed")

    def test_create_folder_invalid_resource_key(self, explorer):
        with pytest.raises(CelError):
            explorer.create_folder("/leading/slash")

    def test_copy_invalid_source(self, explorer):
        with pytest.raises(CelError):
            explorer.copy("\\invalid", "TestExplorer/dest.txt")

    def test_move_invalid_destination(self, explorer):
        explorer.create_file("TestExplorer/hello.txt")
        with pytest.raises(CelError):
            explorer.move("TestExplorer/hello.txt", "\\invalid")

    def test_select_invalid_resource_key(self, explorer):
        with pytest.raises(CelError):
            explorer.select("\\invalid")

    def test_expand_folder_invalid_resource_key(self, explorer):
        with pytest.raises(CelError):
            explorer.expand_folder("\\invalid")

    def test_nested_folder_operations(self, explorer, file):
        explorer.create_folder("TestExplorer/a/b/c")
        explorer.create_file("TestExplorer/a/b/c/deep.txt")
        items = file.list_contents("TestExplorer/a/b/c")
        names = [i["name"] for i in items]
        assert "deep.txt" in names

    def test_create_file_with_cel_extension_rejected(self, explorer):
        # The .cel extension is reserved for project metadata sidecars; agents
        # cannot create files in that namespace directly.
        with pytest.raises(CelError, match="(?i)\\.cel extension is reserved"):
            explorer.create_file("TestExplorer/reserved.cel")

    def test_copy_to_cel_destination_rejected(self, explorer):
        # The same reservation applies to copy destinations.
        explorer.create_file("TestExplorer/source.txt")
        with pytest.raises(CelError, match="(?i)\\.cel extension is reserved"):
            explorer.copy("TestExplorer/source.txt", "TestExplorer/reserved.cel")

    def test_move_to_cel_destination_rejected(self, explorer):
        # Same gate covers non-interactive renames, which route through the
        # copy command in move mode.
        explorer.create_file("TestExplorer/source.txt")
        with pytest.raises(CelError, match="(?i)\\.cel extension is reserved"):
            explorer.move("TestExplorer/source.txt", "TestExplorer/reserved.cel")

    def test_copy_cel_source_reported_as_partial_failure(self, explorer, file, data):
        # A .cel sidecar must never be copied on its own; it would orphan or
        # duplicate the sidecar. The batch runs to completion and reports the
        # .cel source per-resource (partial_failure), so it does not raise; the
        # sidecar stays put and nothing lands at the destination.
        explorer.create_file("TestExplorer/notes.md")
        data.set_field("TestExplorer/notes.md", "priority", json.dumps("high"))

        result = explorer.copy(
            "TestExplorer/notes.md.cel",
            "TestExplorer/copy_target.txt",
        )

        assert result["status"] == "partial_failure"
        messages = " ".join(entry["message"] for entry in result["failedResources"])
        assert "reserved" in messages.lower()
        names = [i["name"] for i in file.list_contents("TestExplorer")]
        assert "copy_target.txt" not in names
        # The sidecar remains paired with its parent.
        assert file.get_info("TestExplorer/notes.md.cel")["type"] == "file"

    def test_move_cel_source_reported_as_partial_failure(self, explorer, file, data):
        # The same per-resource refusal applies to a move (rename) of a lone
        # sidecar key; the sidecar is not relocated.
        explorer.create_file("TestExplorer/notes.md")
        data.set_field("TestExplorer/notes.md", "priority", json.dumps("high"))

        result = explorer.move(
            "TestExplorer/notes.md.cel",
            "TestExplorer/moved_sidecar.txt",
        )

        assert result["status"] == "partial_failure"
        names = [i["name"] for i in file.list_contents("TestExplorer")]
        assert "moved_sidecar.txt" not in names
        assert file.get_info("TestExplorer/notes.md.cel")["type"] == "file"
