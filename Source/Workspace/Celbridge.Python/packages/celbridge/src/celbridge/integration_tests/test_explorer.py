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

    def test_move_preserves_referential_integrity(self, explorer, file, metadata):
        # The reference-rewrite cascade in IResourceFileSystem.MoveAsync must
        # leave no broken project: references after a move.
        file.write(
            "TestExplorer/source.md",
            "Refers to \"project:TestExplorer/target.md\".\n",
        )
        file.write("TestExplorer/target.md", "Target body.\n")

        explorer.move("TestExplorer/target.md", "TestExplorer/renamed.md")

        # No project: reference in our test folder should be broken after the move.
        report = metadata.check_project()
        broken = [
            entry for entry in report.get("brokenReferences", [])
            if entry["source"].startswith("TestExplorer/")
                or entry["missingTarget"].startswith("TestExplorer/")
        ]
        assert broken == [], f"Move broke references: {broken}"

    def test_delete_with_break_references_leaves_dangling_reference(self, explorer, file, metadata):
        # Deleting a referenced resource under break_references should leave
        # the reference dangling, surfaced by metadata_check_project.
        file.write(
            "TestExplorer/has_ref.md",
            "Refers to \"project:TestExplorer/will_delete.md\".\n",
        )
        file.write("TestExplorer/will_delete.md", "Doomed.\n")

        explorer.delete(
            "TestExplorer/will_delete.md",
            reference_policy="break_references",
        )

        report = metadata.check_project()
        broken_targets = {
            entry["missingTarget"]
            for entry in report.get("brokenReferences", [])
        }
        assert "TestExplorer/will_delete.md" in broken_targets

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
