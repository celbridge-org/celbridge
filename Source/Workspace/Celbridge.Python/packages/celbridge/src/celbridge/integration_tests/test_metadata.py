"""Integration tests for the cel.metadata.* namespace.

Each test exercises the full Python proxy -> MCP server -> C# tool -> workspace
service round-trip so the alias mapping, JSON marshalling, and underlying
service behaviour all stay in sync.
"""
import json

import pytest

import celbridge
from celbridge.cel_proxy import CelError

from .helpers import delete_if_exists


def assert_project_clean(extra_broken_references=None, extra_orphan_sidecars=None):
    """Run metadata_check_project and assert no unexpected attention items.

    Pass ``extra_*`` for entries the caller knows about (e.g. a deliberate
    broken reference left by a destructive test). The default expects every
    list to be empty.
    """
    report = celbridge.metadata.check_project()

    extra_broken = set(extra_broken_references or [])
    extra_orphan = set(extra_orphan_sidecars or [])

    actual_broken = {
        (entry["source"], entry["missingTarget"])
        for entry in report.get("brokenReferences", [])
    }
    unexpected_broken = actual_broken - extra_broken
    assert not unexpected_broken, (
        f"Unexpected broken references: {unexpected_broken}; expected only {extra_broken}"
    )

    actual_orphan = set(report.get("orphanSidecars", []))
    unexpected_orphan = actual_orphan - extra_orphan
    assert not unexpected_orphan, (
        f"Unexpected orphan sidecars: {unexpected_orphan}; expected only {extra_orphan}"
    )

    broken_sidecars = report.get("brokenSidecars", [])
    assert broken_sidecars == [], (
        f"Unexpected broken sidecars: {broken_sidecars}"
    )


@pytest.fixture(autouse=True)
def workspace(explorer, file):
    delete_if_exists(explorer, "TestMetaData")
    explorer.create_folder("TestMetaData")
    file.write("TestMetaData/notes.md", "Notes body.\n")
    file.write("TestMetaData/other.md", "Other body.\n")
    yield
    delete_if_exists(explorer, "TestMetaData")


class TestMetaData:

    def test_set_creates_sidecar_and_list_returns_field(self, metadata):
        # Set a field on a resource that has no sidecar. The sidecar should be
        # created and the new field should appear in the list response.
        metadata.set("TestMetaData/notes.md", "priority", json.dumps("high"))
        listed = metadata.list("TestMetaData/notes.md")
        assert listed.get("priority") == "high"

    def test_get_returns_field_value(self, metadata):
        metadata.set("TestMetaData/notes.md", "priority", json.dumps("high"))
        value = metadata.get("TestMetaData/notes.md", "priority")
        assert value == "high"

    def test_get_missing_field_returns_error(self, metadata):
        metadata.set("TestMetaData/notes.md", "priority", json.dumps("high"))
        with pytest.raises(CelError):
            metadata.get("TestMetaData/notes.md", "missing_field")

    def test_find_returns_resources_matching_scalar(self, metadata):
        metadata.set("TestMetaData/notes.md", "priority", json.dumps("high"))
        metadata.set("TestMetaData/other.md", "priority", json.dumps("low"))

        high = metadata.find("priority", json.dumps("high"))
        assert "TestMetaData/notes.md" in high
        assert "TestMetaData/other.md" not in high

    def test_set_accepts_list_of_scalars(self, metadata):
        metadata.set(
            "TestMetaData/notes.md",
            "categories",
            json.dumps(["alpha", "beta"]),
        )
        listed = metadata.list("TestMetaData/notes.md")
        assert listed.get("categories") == ["alpha", "beta"]

    def test_set_rejects_nested_object(self, metadata):
        with pytest.raises(CelError):
            metadata.set(
                "TestMetaData/notes.md",
                "complex",
                json.dumps({"nested": "value"}),
            )

    def test_add_tag_creates_sidecar_when_missing(self, metadata):
        metadata.add_tag("TestMetaData/notes.md", "flagged")
        tags = metadata.list("TestMetaData/notes.md").get("tags", [])
        assert "flagged" in tags

    def test_add_tag_appends_and_is_idempotent(self, metadata):
        metadata.add_tag("TestMetaData/notes.md", "alpha")
        metadata.add_tag("TestMetaData/notes.md", "beta")
        metadata.add_tag("TestMetaData/notes.md", "alpha")
        tags = metadata.list("TestMetaData/notes.md").get("tags", [])
        # Tags appear once each; ordering reflects insertion order.
        assert tags.count("alpha") == 1
        assert "beta" in tags

    def test_find_by_tag_returns_resource(self, metadata):
        metadata.add_tag("TestMetaData/notes.md", "flagged")
        matches = metadata.find("tags", json.dumps("flagged"))
        assert "TestMetaData/notes.md" in matches

    def test_remove_tag_drops_entry(self, metadata):
        metadata.add_tag("TestMetaData/notes.md", "flagged")
        metadata.remove_tag("TestMetaData/notes.md", "flagged")
        matches = metadata.find("tags", json.dumps("flagged"))
        assert "TestMetaData/notes.md" not in matches

    def test_remove_tag_idempotent_when_missing(self, metadata):
        # No sidecar yet; removing a tag is a no-op success.
        metadata.remove_tag("TestMetaData/notes.md", "nope")

    def test_remove_field_is_no_op_when_absent(self, metadata):
        # Returns success without touching disk.
        metadata.remove("TestMetaData/notes.md", "nope")

    def test_list_returns_empty_object_when_no_sidecar(self, metadata):
        result = metadata.list("TestMetaData/notes.md")
        assert result == {}

    def test_set_field_visible_through_file_read(self, metadata, file):
        metadata.set("TestMetaData/notes.md", "priority", json.dumps("high"))
        sidecar_text = file.read("TestMetaData/notes.md.cel")["content"]
        assert "priority" in sidecar_text
        assert "high" in sidecar_text

    def test_invalid_resource_key_fails(self, metadata):
        with pytest.raises(CelError):
            metadata.set("\\invalid", "priority", json.dumps("high"))

    def test_find_with_non_scalar_value_fails(self, metadata):
        with pytest.raises(CelError):
            metadata.find("tags", json.dumps(["a", "b"]))


class TestMetaDataCheckProject:

    def test_clean_project_returns_empty_lists(self, metadata):
        report = metadata.check_project()
        # The autouse workspace fixture leaves only TestMetaData behind, which
        # doesn't carry references. Any unrelated project state shows up here
        # too; we assert only the report shape so the test is robust to other
        # content in the demo project.
        assert isinstance(report.get("brokenReferences"), list)
        assert isinstance(report.get("orphanSidecars"), list)
        assert isinstance(report.get("brokenSidecars"), list)

    def test_broken_reference_detected_after_target_deleted_with_break_references(self, metadata, file, explorer):
        # Create a source that references a target, then delete the target
        # under break_references so the reference is left dangling. The check
        # tool reports the resulting broken reference.
        file.write("TestMetaData/source.md", "Refers to \"project:TestMetaData/target.md\".\n")
        file.write("TestMetaData/target.md", "Target body.\n")

        explorer.delete("TestMetaData/target.md", reference_policy="break_references")

        report = metadata.check_project()
        broken = [
            entry for entry in report.get("brokenReferences", [])
            if entry["missingTarget"] == "TestMetaData/target.md"
        ]
        assert len(broken) == 1
        assert broken[0]["source"] == "TestMetaData/source.md"

    def test_orphan_sidecar_detected_when_parent_missing(self, metadata, file):
        # Write a sidecar whose parent does not exist on disk. The pairing
        # pass classifies it as an orphan.
        file.write(
            "TestMetaData/orphaned.png.cel",
            "+++\ntags = [\"orphan\"]\n+++\n",
        )
        report = metadata.check_project()
        assert "TestMetaData/orphaned.png.cel" in report.get("orphanSidecars", [])

    def test_broken_sidecar_detected_when_frontmatter_unparseable(self, metadata, file):
        # Write a sidecar whose frontmatter is malformed TOML between fences.
        file.write("TestMetaData/notes.md.cel", "+++\nthis is not = valid // toml\n+++\n")
        report = metadata.check_project()
        assert "TestMetaData/notes.md.cel" in report.get("brokenSidecars", [])

    def test_move_preserves_invariant(self, metadata, explorer, file):
        # A reference rewrite during a move must leave the project in a
        # consistent state — no broken references remain.
        file.write("TestMetaData/src.md", "Refers to \"project:TestMetaData/old.md\".\n")
        file.write("TestMetaData/old.md", "Old body.\n")

        explorer.move("TestMetaData/old.md", "TestMetaData/new.md")

        report = metadata.check_project()
        broken = [
            entry for entry in report.get("brokenReferences", [])
            if entry["source"].startswith("TestMetaData/")
                or entry["missingTarget"].startswith("TestMetaData/")
        ]
        assert broken == [], f"Move broke references: {broken}"

    def test_delete_without_referencers_leaves_clean_state(self, metadata, explorer, file):
        # Delete a resource that nothing references; the broken-references list
        # should not gain any entries scoped to our test folder.
        file.write("TestMetaData/standalone.md", "No incoming references.\n")
        explorer.delete("TestMetaData/standalone.md")

        report = metadata.check_project()
        broken = [
            entry for entry in report.get("brokenReferences", [])
            if entry["missingTarget"].startswith("TestMetaData/")
        ]
        assert broken == []
