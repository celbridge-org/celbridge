"""Integration tests for the resource policy contract through the MCP tools.

Drives the agent-facing file.* and explorer.* tools against the live project to
confirm the resource policy is enforced end to end:

- the system tier (the reserved .celbridge metadata folder is denied; the
  *.celbridge project file stays allowed),
- the Phase 4.5 destination-visibility gate (create / write / move / copy to a
  path the ignore-file hides is refused, not silently written to disk where
  Celbridge could never see it again),
- the remove set (the ignore-file itself is dropped from the resource set).

The system-tier, visibility-gate, and remove assertions rely only on the
defaults every project template ships: ignore-file = ".gitignore" with the
standard noise patterns (*.tmp, *.bak, node_modules/, ...) and
remove = [".gitignore"].

Lock and add coverage needs a project whose [resources] config declares them.
The policy compiles once at workspace load and no tool recompiles it at runtime
(soft-reload is follow-up item 3), so those tests self-skip unless the fixture
resources are present. The per-class docstrings document the required config.
"""
import pytest

from celbridge.cel_proxy import CelError

from .helpers import delete_if_exists


def _root_child_names(file):
    tree = file.get_tree("", depth=1)
    return [child["name"] for child in tree.get("children", [])]


def _resource_exists(file, resource):
    try:
        file.get_info(resource)
        return True
    except CelError:
        return False


@pytest.fixture(autouse=True)
def workspace(explorer):
    delete_if_exists(explorer, "TestPolicy")
    explorer.create_folder("TestPolicy")
    yield
    delete_if_exists(explorer, "TestPolicy")


class TestResourcePolicy:
    """System tier, ignore-file visibility gate, and the remove set.

    Relies only on the template defaults (.gitignore ignore-file with the
    standard noise patterns, remove = [".gitignore"]).
    """

    def test_metadata_folder_hidden(self, file):
        # .celbridge is system-denied: never a resource, never in the tree.
        assert ".celbridge" not in _root_child_names(file)

    def test_project_file_visible(self, file):
        # The *.celbridge project file is system-allowed and stays visible even
        # though the ignore-file and remove sets never mention it.
        names = _root_child_names(file)
        assert any(name.endswith(".celbridge") for name in names)

    def test_write_into_metadata_folder_denied(self, file):
        with pytest.raises(CelError, match="(?i)denied"):
            file.write(".celbridge/probe.txt", "nope")

    def test_create_ignored_temp_file_denied(self, explorer, file):
        # The *.tmp pattern hides the destination, so creating it as a resource
        # is refused rather than writing a file Celbridge could never see.
        with pytest.raises(CelError):
            explorer.create_file("TestPolicy/scratch.tmp")
        names = [i["name"] for i in file.list_contents("TestPolicy")]
        assert "scratch.tmp" not in names

    def test_write_ignored_backup_file_denied(self, file):
        with pytest.raises(CelError, match="(?i)denied"):
            file.write("TestPolicy/notes.bak", "nope")

    def test_create_ignored_folder_denied(self, explorer):
        # node_modules/ is a directory pattern in the template ignore-file.
        with pytest.raises(CelError):
            explorer.create_folder("TestPolicy/node_modules")

    def test_move_to_ignored_destination_denied(self, explorer, file):
        explorer.create_file("TestPolicy/keep.txt")
        # The move batch runs to completion and reports the refused destination
        # per-resource (status partial_failure), so it does not raise; the
        # source stays put and nothing lands at the ignored destination.
        result = explorer.move("TestPolicy/keep.txt", "TestPolicy/keep.tmp")
        assert result["status"] == "partial_failure"
        names = [i["name"] for i in file.list_contents("TestPolicy")]
        assert "keep.txt" in names
        assert "keep.tmp" not in names

    def test_copy_to_ignored_destination_denied(self, explorer, file):
        explorer.create_file("TestPolicy/keep.txt")
        result = explorer.copy("TestPolicy/keep.txt", "TestPolicy/keep.bak")
        assert result["status"] == "partial_failure"
        names = [i["name"] for i in file.list_contents("TestPolicy")]
        assert "keep.bak" not in names

    def test_gitignore_hidden_from_tree(self, file):
        # The template hides the ignore-file itself via remove = [".gitignore"].
        assert ".gitignore" not in _root_child_names(file)

    def test_create_removed_file_denied(self, file):
        # remove = [".gitignore"] is a bare-name pattern, so it drops a
        # .gitignore at any depth from the resource set; writing one under our
        # test folder is refused. Targeting TestPolicy keeps the real project
        # .gitignore untouched if the remove entry has been edited away.
        with pytest.raises(CelError, match="(?i)denied"):
            file.write("TestPolicy/.gitignore", "*.log\n")


_LOCK_FIXTURE_FILE = "PolicyFixture/locked.txt"
_LOCK_FIXTURE_FOLDER = "PolicyFixture"


class TestResourcePolicyLock:
    """Lock-axis enforcement through the tools.

    Requires the loaded project's [resources] config to lock a fixture
    resource:

        lock = ["PolicyFixture/locked.txt"]

    with PolicyFixture/locked.txt present on disk. Because policy compiles at
    workspace load and no tool recompiles it at runtime (follow-up item 3),
    these tests self-skip when the fixture is absent.
    """

    @pytest.fixture(autouse=True)
    def require_lock_fixture(self, file):
        if not _resource_exists(file, _LOCK_FIXTURE_FILE):
            pytest.skip(
                f"requires locked fixture '{_LOCK_FIXTURE_FILE}' and "
                "[resources].lock config (policy is load-time only; item 3)"
            )

    def test_write_locked_file_denied(self, file):
        # file.write surfaces the gateway failure directly, so a locked target
        # raises rather than silently succeeding.
        with pytest.raises(CelError, match="(?i)denied|lock"):
            file.write(_LOCK_FIXTURE_FILE, "mutate")

    def test_move_locked_file_denied(self, explorer, file):
        # The move batch runs to completion and reports the lock refusal
        # per-resource (status partial_failure), consistent with delete; it does
        # not raise. The locked source stays put and no relocated copy appears.
        result = explorer.move(_LOCK_FIXTURE_FILE, "PolicyFixture/relocated.txt")
        assert result["status"] == "partial_failure"
        assert _resource_exists(file, _LOCK_FIXTURE_FILE)
        assert not _resource_exists(file, "PolicyFixture/relocated.txt")

    def test_delete_locked_file_denied(self, explorer, file):
        # The delete command runs the batch to completion and reports the lock
        # refusal per-resource rather than failing the whole call, so it does
        # not raise; the resource must simply survive, not be deleted.
        result = explorer.delete(_LOCK_FIXTURE_FILE)
        assert _resource_exists(file, _LOCK_FIXTURE_FILE)
        outcomes = {r["outcome"] for r in result["resourceResults"]}
        assert "Deleted" not in outcomes

    def test_delete_path_frozen_folder_denied(self, explorer, file):
        # The folder holds a locked descendant, so the descendant-lock cascade
        # refuses to delete the whole subtree. Same per-resource reporting as a
        # locked file: a payload, not an error.
        result = explorer.delete(_LOCK_FIXTURE_FOLDER)
        assert _resource_exists(file, _LOCK_FIXTURE_FOLDER)
        assert _resource_exists(file, _LOCK_FIXTURE_FILE)
        outcomes = {r["outcome"] for r in result["resourceResults"]}
        assert "Deleted" not in outcomes


_ADD_FIXTURE_FILE = "PolicyFixture/.venv/added.txt"


class TestResourcePolicyAdd:
    """Add-set resurfacing through the tools.

    Requires the loaded project's [resources] config to re-include one
    otherwise-ignored fixture file by name:

        add = ["PolicyFixture/.venv/added.txt"]

    with the ignore-file hiding .venv, PolicyFixture/.venv/added.txt present,
    and a sibling PolicyFixture/.venv/hidden.txt that add does NOT name. The
    pattern names the single file rather than .venv/** so the sibling stays
    hidden and the test can prove the resurfacing is selective. Self-skips when
    the fixture is absent (policy is load-time only; follow-up item 3).
    """

    def test_added_resource_visible_and_readable(self, file):
        if not _resource_exists(file, _ADD_FIXTURE_FILE):
            pytest.skip(
                f"requires added fixture '{_ADD_FIXTURE_FILE}' and "
                "[resources].add config (policy is load-time only; item 3)"
            )
        # add re-includes this file even though the ignore-file hides .venv.
        result = file.read(_ADD_FIXTURE_FILE)
        assert "content" in result
        # A sibling in the same ignored folder that add does not name stays
        # hidden, proving the resurfacing is selective, not an un-ignore of .venv.
        assert not _resource_exists(file, "PolicyFixture/.venv/hidden.txt")
