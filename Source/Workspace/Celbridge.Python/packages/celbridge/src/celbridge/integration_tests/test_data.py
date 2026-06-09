"""Integration tests for the cel.data.* namespace.

Each test exercises the full Python proxy -> MCP server -> C# tool -> workspace
service round-trip so the alias mapping, JSON marshalling, and underlying
service behaviour all stay in sync.
"""
import json

import pytest

import celbridge
from celbridge.cel_proxy import CelError

from .helpers import delete_if_exists


def _fields(*pairs):
    """Build the fields-JSON payload for data.set_fields from name/value pairs.

    Each value is JSON-encoded individually (the value_json convention) and the
    outer dict is JSON-encoded as the tool argument.
    """
    return json.dumps({name: json.dumps(value) for name, value in pairs})


def _get_field_value(data, resource, name):
    """Convenience wrapper that returns the value for a single named field, or
    None when the field is absent or the sidecar does not exist."""
    try:
        results = data.get_fields(resource, json.dumps([name]))
    except CelError:
        return None
    for record in results:
        if record["name"] == name and record.get("found"):
            return record.get("value")
    return None


def _get_all_fields(data, resource):
    """Return a {name: value} dict of all visible fields on the sidecar. Returns
    an empty dict when the sidecar is absent. Hides the reserved namespace
    (data_get_fields already filters underscore-prefixed names)."""
    try:
        results = data.get_fields(resource, json.dumps(["*"]))
    except CelError:
        return {}
    return {record["name"]: record.get("value") for record in results if record.get("found")}


@pytest.fixture(autouse=True)
def workspace(explorer, file):
    delete_if_exists(explorer, "TestData")
    explorer.create_folder("TestData")
    file.write("TestData/notes.md", "Notes body.\n")
    file.write("TestData/other.md", "Other body.\n")
    yield
    delete_if_exists(explorer, "TestData")


class TestData:

    def test_set_fields_creates_sidecar_and_get_fields_returns_field(self, data):
        # Set a field on a resource that has no sidecar. The sidecar is created
        # and the new field appears via data_get_fields.
        data.set_fields("TestData/notes.md", _fields(("priority", "high")))
        assert _get_field_value(data, "TestData/notes.md", "priority") == "high"

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
        assert _get_field_value(data, "TestData/notes.md", "categories") == ["alpha", "beta"]

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
        assert _get_field_value(data, "TestData/notes.md", "valid") is None

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
        # Verify via data_inspect's tag surfacing rather than poking at the
        # reserved _tags field directly.
        report = data.inspect(json.dumps(["TestData/notes.md"]))
        record = report["results"][0]
        assert "flagged" in record.get("tags", [])

    def test_add_tags_batch_appends_in_one_call(self, data):
        data.add_tags("TestData/notes.md", json.dumps(["alpha", "beta", "gamma"]))
        report = data.inspect(json.dumps(["TestData/notes.md"]))
        tags = report["results"][0].get("tags", [])
        for tag in ("alpha", "beta", "gamma"):
            assert tags.count(tag) == 1

    def test_add_tags_idempotent_for_already_present(self, data):
        data.add_tags("TestData/notes.md", json.dumps(["alpha"]))
        data.add_tags("TestData/notes.md", json.dumps(["alpha", "beta"]))
        report = data.inspect(json.dumps(["TestData/notes.md"]))
        tags = report["results"][0].get("tags", [])
        assert tags.count("alpha") == 1
        assert "beta" in tags

    def test_find_tag_returns_resource(self, data):
        data.add_tags("TestData/notes.md", json.dumps(["flagged"]))
        matches = data.find_tag("flagged")
        # Tool responses emit resource keys in canonical "root:path" form.
        assert "project:TestData/notes.md" in matches

    def test_list_tags_returns_known_values(self, data):
        data.add_tags("TestData/notes.md", json.dumps(["alpha"]))
        data.add_tags("TestData/other.md", json.dumps(["beta"]))
        report = data.list_tags()
        tags = report.get("tags", [])
        assert "alpha" in tags
        assert "beta" in tags

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
        fields = _get_all_fields(data, "TestData/notes.md")
        assert "a" not in fields
        assert "b" not in fields
        assert fields.get("c") == "z"

    def test_inspect_returns_no_sidecar_when_absent(self, data):
        report = data.inspect(json.dumps(["TestData/notes.md"]))
        record = report["results"][0]
        assert record["status"] == "NoSidecar"

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


class TestDataInspect:

    def test_clean_project_returns_expected_shape(self, data):
        # The autouse workspace fixture leaves only TestData behind; any
        # unrelated project state shows up too. Assert only the envelope so
        # the test is robust to other content in the demo project.
        report = data.inspect()
        assert isinstance(report.get("results"), list)
        summary = report.get("summary", {})
        for key in ("healthy", "broken", "orphan", "invalidSidecar", "noSidecar"):
            assert isinstance(summary.get(key), int)

    def test_single_resource_returns_array_of_one(self, data):
        data.set_fields("TestData/notes.md", _fields(("priority", "high")))
        report = data.inspect(json.dumps(["TestData/notes.md"]))
        assert len(report["results"]) == 1
        record = report["results"][0]
        assert record["resource"] == "project:TestData/notes.md"
        assert record["status"] == "Healthy"

    def test_summary_only_omits_tags_and_fields(self, data):
        data.set_fields("TestData/notes.md", _fields(("priority", "high")))
        data.add_tags("TestData/notes.md", json.dumps(["flagged"]))
        report = data.inspect(json.dumps(["TestData/notes.md"]), summary_only=True)
        record = report["results"][0]
        assert "tags" not in record
        assert "fields" not in record
        assert record["status"] == "Healthy"

    def test_orphan_cel_file_detected_when_parent_missing(self, data, file):
        # Write a sidecar whose parent does not exist on disk. The pairing
        # pass classifies it as an orphan.
        file.write("TestData/orphaned.png.cel", "_tags = [\"orphan\"]\n")
        report = data.inspect(json.dumps(["TestData/orphaned.png.cel"]))
        record = report["results"][0]
        assert record["status"] == "Orphan"

    def test_broken_cel_file_detected_when_unparseable(self, data, file):
        # Write a sidecar whose TOML is malformed.
        file.write("TestData/notes.md.cel", "this is not = valid // toml")
        report = data.inspect(json.dumps(["TestData/notes.md"]))
        record = report["results"][0]
        assert record["status"] == "Broken"
        assert "parseError" in record

    def test_file_write_can_repair_broken_sidecar(self, data, file):
        # data_set_fields refuses to overwrite a Broken sidecar — file.write is
        # the only path back to Healthy.
        file.write("TestData/notes.md.cel", "this = is = not = valid toml ###")
        broken = data.inspect(json.dumps(["TestData/notes.md"]))
        assert broken["results"][0]["status"] == "Broken"

        file.write("TestData/notes.md.cel", "priority = \"low\"\n")
        healed = data.inspect(json.dumps(["TestData/notes.md"]))
        assert healed["results"][0]["status"] == "Healthy"

    def test_inspect_path_anchored_glob_matches_scoped_folder(self, data, file):
        # Regression: path-anchored globs used to return empty results because
        # the matcher compared against the canonical "project:foo" form.
        file.write("TestData/notes.md.cel", "priority = \"high\"\n")
        report = data.inspect("[]", pattern="TestData/**")
        resources = [r["resource"] for r in report["results"]]
        assert any(r.endswith("TestData/notes.md") for r in resources)

    def test_inspect_double_star_does_not_duplicate_sidecar_and_parent(self, data, file):
        # Regression: ** pattern used to surface both the parent and the
        # healthy-sidecar key as separate records.
        file.write("TestData/notes.md.cel", "priority = \"high\"\n")
        report = data.inspect("[]", pattern="**")
        resources = [r["resource"] for r in report["results"]]
        assert "project:TestData/notes.md.cel" not in resources


class TestDataCheckReferences:

    def test_clean_state_returns_empty_references(self, data, file, explorer):
        # Baseline: write a source and its target so the reference resolves.
        # The references array should not list this pair.
        file.write("TestData/source.json", "{\"target\": \"project:TestData/target.json\"}")
        file.write("TestData/target.json", "{}")
        report = data.check_references()
        refs = report.get("references", [])
        for entry in refs:
            assert not (
                entry["source"] == "project:TestData/source.json"
                and entry["missingTarget"] == "project:TestData/target.json"
            )

    def test_dangling_reference_surfaces_with_canonical_source_and_target(self, data, file):
        # Create a referencer pointing at a target that doesn't exist.
        file.write(
            "TestData/dangling_source.json",
            "{\"target\": \"project:TestData/never_existed.json\"}",
        )
        report = data.check_references()
        match = next(
            (
                entry for entry in report["references"]
                if entry["source"] == "project:TestData/dangling_source.json"
                and entry["missingTarget"] == "project:TestData/never_existed.json"
            ),
            None,
        )
        assert match is not None

    def test_restoring_target_clears_the_reference(self, data, file):
        # Plant a dangling reference, then create the target. Next call should
        # not list the now-resolved pair.
        file.write(
            "TestData/heals_source.json",
            "{\"target\": \"project:TestData/heals_target.json\"}",
        )
        before = data.check_references()
        assert any(
            entry["source"] == "project:TestData/heals_source.json"
            and entry["missingTarget"] == "project:TestData/heals_target.json"
            for entry in before["references"]
        )

        file.write("TestData/heals_target.json", "{}")
        after = data.check_references()
        assert not any(
            entry["source"] == "project:TestData/heals_source.json"
            and entry["missingTarget"] == "project:TestData/heals_target.json"
            for entry in after["references"]
        )

    def test_off_allowlist_files_are_invisible(self, data, file):
        # A .md file is not on the scanner allowlist. Its quoted reference is
        # descriptive prose, not a tracked reference; data_check_references
        # should not surface it as a broken-reference source.
        file.write(
            "TestData/notes.md",
            "Documentation mentioning \"project:TestData/never_existed_md.json\" descriptively.",
        )
        report = data.check_references()
        offenders = [
            entry for entry in report["references"]
            if entry["source"] == "project:TestData/notes.md"
        ]
        assert offenders == []
