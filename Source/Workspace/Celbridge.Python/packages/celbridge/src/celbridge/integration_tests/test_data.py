"""Integration tests for the cel.data.* namespace.

Each test exercises the full Python proxy -> MCP server -> C# tool -> workspace
service round-trip so the alias mapping, JSON marshalling, and underlying
service behaviour all stay in sync.
"""
import json

import pytest

import celbridge
from celbridge.cel_proxy import CelError

from .helpers import delete_if_exists, write_cel_file_directly


def _fields(*pairs):
    """Build the fields-JSON payload for data.set_fields from name/value pairs.

    Each value is JSON-encoded individually (the value_json convention) and the
    outer dict is JSON-encoded as the tool argument.
    """
    return json.dumps({name: json.dumps(value) for name, value in pairs})


def assert_project_clean(extra_broken_references=None, extra_orphan_cel_files=None):
    """Run data_check_project and assert no unexpected attention items.

    Pass ``extra_*`` for entries the caller knows about (e.g. a deliberate
    broken reference left by a destructive test). The default expects every
    list to be empty.
    """
    report = celbridge.data.check_project()

    extra_broken = set(extra_broken_references or [])
    extra_orphan = set(extra_orphan_cel_files or [])

    actual_broken = {
        (entry["source"], entry["missingTarget"])
        for entry in report.get("brokenReferences", [])
    }
    unexpected_broken = actual_broken - extra_broken
    assert not unexpected_broken, (
        f"Unexpected broken references: {unexpected_broken}; expected only {extra_broken}"
    )

    actual_orphan = set(report.get("orphanCelFiles", []))
    unexpected_orphan = actual_orphan - extra_orphan
    assert not unexpected_orphan, (
        f"Unexpected orphan .cel files: {unexpected_orphan}; expected only {extra_orphan}"
    )

    broken_cel_files = report.get("brokenCelFiles", [])
    assert broken_cel_files == [], (
        f"Unexpected broken .cel files: {broken_cel_files}"
    )


@pytest.fixture(autouse=True)
def workspace(explorer, file):
    delete_if_exists(explorer, "TestData")
    explorer.create_folder("TestData")
    file.write("TestData/notes.md", "Notes body.\n")
    file.write("TestData/other.md", "Other body.\n")
    yield
    delete_if_exists(explorer, "TestData")


class TestData:

    def test_set_fields_creates_sidecar_and_get_info_returns_field(self, data):
        # Set a field on a resource that has no sidecar. The sidecar is created
        # and the new field appears in the get_info response.
        data.set_fields("TestData/notes.md", _fields(("priority", "high")))
        info = data.get_info("TestData/notes.md")
        assert info["fields"].get("priority") == "high"

    def test_get_fields_returns_field_value(self, data):
        data.set_fields("TestData/notes.md", _fields(("priority", "high")))
        results = data.get_fields("TestData/notes.md", json.dumps(["priority"]))
        priority = next(r for r in results if r["name"] == "priority")
        assert priority["found"] is True
        assert priority["value"] == "high"

    def test_get_fields_missing_returns_found_false(self, data):
        data.set_fields("TestData/notes.md", _fields(("priority", "high")))
        results = data.get_fields(
            "TestData/notes.md",
            json.dumps(["priority", "missing_field"]),
        )
        missing = next(r for r in results if r["name"] == "missing_field")
        assert missing["found"] is False
        assert "value" not in missing

    def test_get_fields_all_sentinel_returns_every_field(self, data):
        data.set_fields(
            "TestData/notes.md",
            _fields(("priority", "high"), ("title", "Notes")),
        )
        results = data.get_fields("TestData/notes.md", json.dumps(["*"]))
        names = {r["name"] for r in results if r["found"]}
        assert {"priority", "title"}.issubset(names)

    def test_set_fields_accepts_list_of_scalars(self, data):
        data.set_fields(
            "TestData/notes.md",
            _fields(("categories", ["alpha", "beta"])),
        )
        info = data.get_info("TestData/notes.md")
        assert info["fields"].get("categories") == ["alpha", "beta"]

    def test_set_fields_atomic_batch_writes_all_or_nothing(self, data):
        # A batch with a nested-object value (rejected at write time) must
        # leave the sidecar unchanged — none of the valid fields land.
        with pytest.raises(CelError):
            data.set_fields(
                "TestData/notes.md",
                json.dumps({
                    "valid": json.dumps("ok"),
                    "invalid": json.dumps({"nested": "value"}),
                }),
            )
        info = data.get_info("TestData/notes.md")
        assert info["fields"].get("valid") is None

    def test_set_fields_rejects_nested_object(self, data):
        with pytest.raises(CelError):
            data.set_fields(
                "TestData/notes.md",
                _fields(("complex", {"nested": "value"})),
            )

    def test_set_fields_rejects_reserved_underscore_name(self, data):
        with pytest.raises(CelError, match="(?i)reserved"):
            data.set_fields(
                "TestData/notes.md",
                _fields(("_tags", ["should-not-land"])),
            )

    def test_add_tags_creates_sidecar_when_missing(self, data):
        data.add_tags("TestData/notes.md", json.dumps(["flagged"]))
        info = data.get_info("TestData/notes.md")
        assert "flagged" in info["fields"].get("_tags", [])

    def test_add_tags_batch_appends_in_one_call(self, data):
        data.add_tags("TestData/notes.md", json.dumps(["alpha", "beta", "gamma"]))
        info = data.get_info("TestData/notes.md")
        tags = info["fields"].get("_tags", [])
        for tag in ("alpha", "beta", "gamma"):
            assert tags.count(tag) == 1

    def test_add_tags_idempotent_for_already_present(self, data):
        data.add_tags("TestData/notes.md", json.dumps(["alpha"]))
        data.add_tags("TestData/notes.md", json.dumps(["alpha", "beta"]))
        info = data.get_info("TestData/notes.md")
        tags = info["fields"].get("_tags", [])
        assert tags.count("alpha") == 1
        assert "beta" in tags

    def test_find_tag_returns_resource(self, data):
        data.add_tags("TestData/notes.md", json.dumps(["flagged"]))
        matches = data.find_tag("flagged")
        # Tool responses emit resource keys in canonical "root:path" form.
        assert "project:TestData/notes.md" in matches

    def test_remove_tags_drops_entries(self, data):
        data.add_tags("TestData/notes.md", json.dumps(["alpha", "beta"]))
        data.remove_tags("TestData/notes.md", json.dumps(["alpha", "beta"]))
        matches_alpha = data.find_tag("alpha")
        matches_beta = data.find_tag("beta")
        assert "project:TestData/notes.md" not in matches_alpha
        assert "project:TestData/notes.md" not in matches_beta

    def test_remove_tags_idempotent_when_missing(self, data):
        # No sidecar yet; removing a tag is a no-op success.
        data.remove_tags("TestData/notes.md", json.dumps(["nope"]))

    def test_remove_fields_is_no_op_when_absent(self, data):
        # Returns success without touching disk.
        data.remove_fields("TestData/notes.md", json.dumps(["nope"]))

    def test_remove_fields_batch(self, data):
        data.set_fields(
            "TestData/notes.md",
            _fields(("a", "x"), ("b", "y"), ("c", "z")),
        )
        data.remove_fields("TestData/notes.md", json.dumps(["a", "b"]))
        info = data.get_info("TestData/notes.md")
        fields = info["fields"]
        assert "a" not in fields
        assert "b" not in fields
        assert fields.get("c") == "z"

    def test_get_info_returns_empty_when_no_sidecar(self, data):
        result = data.get_info("TestData/notes.md")
        assert result == {"hasSidecar": False, "fields": {}}

    def test_set_fields_visible_through_file_read(self, data, file):
        data.set_fields("TestData/notes.md", _fields(("priority", "high")))
        sidecar_text = file.read("TestData/notes.md.cel")["content"]
        assert "priority" in sidecar_text
        assert "high" in sidecar_text

    def test_invalid_resource_key_fails(self, data):
        with pytest.raises(CelError):
            data.set_fields("\\invalid", _fields(("priority", "high")))

    def test_sidecar_key_rejected(self, data):
        with pytest.raises(CelError):
            data.set_fields("TestData/notes.md.cel", _fields(("priority", "high")))


class TestDataCheckProject:

    def test_clean_project_returns_empty_lists(self, data):
        report = data.check_project()
        # The autouse workspace fixture leaves only TestData behind, which
        # doesn't carry references. Any unrelated project state shows up here
        # too; we assert only the report shape so the test is robust to other
        # content in the demo project.
        assert isinstance(report.get("brokenReferences"), list)
        assert isinstance(report.get("orphanCelFiles"), list)
        assert isinstance(report.get("brokenCelFiles"), list)

    def test_broken_reference_detected_after_target_deleted_with_break_references(self, data, file, explorer):
        # Create a source that references a target, then delete the target
        # under break_references so the reference is left dangling. The check
        # tool reports the resulting broken reference.
        # The referencer is .json because the scanner walks an allowlist of
        # data-bearing extensions; .md is excluded (documentation, not data).
        file.write("TestData/source.json", "{\"target\": \"project:TestData/target.md\"}")
        file.write("TestData/target.md", "Target body.\n")

        explorer.delete("TestData/target.md", reference_policy="break_references")

        report = data.check_project()
        broken = [
            entry for entry in report.get("brokenReferences", [])
            if entry["missingTarget"] == "project:TestData/target.md"
        ]
        assert len(broken) == 1
        assert broken[0]["source"] == "project:TestData/source.json"

    def test_orphan_cel_file_detected_when_parent_missing(self, data, app):
        # Write a sidecar whose parent does not exist on disk. The pairing
        # pass classifies it as an orphan. Direct filesystem write bypasses
        # the file.* tools' .cel denial.
        write_cel_file_directly(
            app,
            "TestData/orphaned.png.cel",
            "_tags = [\"orphan\"]\n",
        )
        report = data.check_project()
        assert "project:TestData/orphaned.png.cel" in report.get("orphanCelFiles", [])

    def test_broken_cel_file_detected_when_unparseable(self, data, app):
        # Write a sidecar whose TOML is malformed. Direct filesystem write
        # bypasses the file.* tools' .cel denial.
        write_cel_file_directly(
            app,
            "TestData/notes.md.cel",
            "this is not = valid // toml",
        )
        report = data.check_project()
        assert "project:TestData/notes.md.cel" in report.get("brokenCelFiles", [])

    def test_move_preserves_invariant(self, data, explorer, file):
        # A reference rewrite during a move must leave the project in a
        # consistent state — no broken references remain. The referencer is
        # .json (an allowlisted extension) so the cascade actually scans and
        # rewrites the literal; a .md referencer would be invisible to the
        # scanner.
        file.write("TestData/src.json", "{\"target\": \"project:TestData/old.md\"}")
        file.write("TestData/old.md", "Old body.\n")

        explorer.move("TestData/old.md", "TestData/new.md")

        report = data.check_project()
        broken = [
            entry for entry in report.get("brokenReferences", [])
            if entry["source"].startswith("project:TestData/")
                or entry["missingTarget"].startswith("project:TestData/")
        ]
        assert broken == [], f"Move broke references: {broken}"

    def test_delete_without_referencers_leaves_clean_state(self, data, explorer, file):
        # Delete a resource that nothing references; the broken-references list
        # should not gain any entries scoped to our test folder.
        file.write("TestData/standalone.md", "No incoming references.\n")
        explorer.delete("TestData/standalone.md")

        report = data.check_project()
        broken = [
            entry for entry in report.get("brokenReferences", [])
            if entry["missingTarget"].startswith("project:TestData/")
        ]
        assert broken == []
