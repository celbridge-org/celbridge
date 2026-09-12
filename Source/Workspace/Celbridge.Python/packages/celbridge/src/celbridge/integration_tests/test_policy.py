"""Integration tests for the resource policy contract through the MCP tools.

Drives the agent-facing file.* and explorer.* tools against the live project to
confirm the policy behaves end to end:

- the system tier (the reserved .celbridge metadata folder is denied; the
  *.celbridge project file is an ordinary resource that cannot be moved out of
  the project folder or renamed away from its extension),
- that 'hide' is cosmetic (a hidden resource stays visible to the tools, and
  stays readable and writable), which is the contract the Explorer's hide
  patterns are allowed to assume.

Everything here relies on the defaults every project template ships, and the
hide assertions hold whether or not the loaded project still lists .gitignore
under 'hide' — a hidden resource and an unhidden one are both visible to the
tools, which is the point.

Not covered here: that 'search-exclude' bounds search. Asserting it end to end
needs fixture content inside an excluded folder, and the excluded path cannot
be built through the tools while it is excluded from the tree. The policy-level
behaviour is covered by ResourcePolicyTests and ResourcePatternSetTests.
"""
import pytest

from celbridge.cel_proxy import CelError

from .helpers import delete_if_exists


def _root_child_names(file):
    tree = file.get_tree("", depth=1)
    return [child["name"] for child in tree.get("children", [])]


def _project_file_name(file):
    names = _root_child_names(file)
    for name in names:
        if name.endswith(".celbridge"):
            return name

    return None


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


class TestResourcePolicySystemTier:
    """The non-configurable floor: reserved folders and the project file."""

    def test_metadata_folder_hidden(self, file):
        # .celbridge is system-denied: never a resource, never in the tree.
        assert ".celbridge" not in _root_child_names(file)

    def test_write_into_metadata_folder_denied(self, file):
        with pytest.raises(CelError, match="(?i)denied"):
            file.write(".celbridge/probe.txt", "nope")

    def test_project_file_visible(self, file):
        # The project file opens as a document, in the Project Settings editor or the code editor, so
        # hiding it from the tree would leave it the one document with no way in.
        assert _project_file_name(file) is not None

    def test_project_file_move_out_of_folder_refused(self, explorer, file):
        # The project folder is defined as the folder the project file sits in, so moving the file
        # does not relocate the project, it orphans it.
        project_file = _project_file_name(file)
        result = explorer.move(project_file, f"TestPolicy/{project_file}")
        assert result["status"] == "partial_failure"
        assert "moved out of it" in result["failedResources"][0]["message"]
        assert _resource_exists(file, project_file)

    def test_project_file_extension_change_refused(self, explorer, file):
        # The file picker and file activation both find a project by its extension, so a rename that
        # drops it leaves a project nothing can open again.
        project_file = _project_file_name(file)
        renamed = project_file.replace(".celbridge", ".txt")
        result = explorer.move(project_file, renamed)
        assert result["status"] == "partial_failure"
        assert ".celbridge extension" in result["failedResources"][0]["message"]
        assert _resource_exists(file, project_file)


class TestResourcePolicyHideIsCosmetic:
    """'hide' binds the Explorer tree and nothing else.

    The templates ship hide = [".gitignore"], so the project .gitignore is the
    natural subject. These assertions hold either way: whether or not it is
    still listed under 'hide', the tools see it.
    """

    def test_hidden_file_is_in_the_tool_tree(self, file):
        # The Explorer draws its tree from the registry and filters hidden rows in the view. The tools
        # read through the gateway, which applies the system tier alone.
        assert ".gitignore" in _root_child_names(file)

    def test_hidden_file_is_readable(self, file):
        result = file.read(".gitignore")
        assert "content" in result

    def test_hidden_file_is_writable(self, explorer, file):
        # Targets our own folder so the project .gitignore is untouched.
        file.write("TestPolicy/.gitignore", "*.log\n")
        assert _resource_exists(file, "TestPolicy/.gitignore")
        assert file.read("TestPolicy/.gitignore")["content"] == "*.log\n"

    def test_hidden_file_can_be_moved_and_deleted(self, explorer, file):
        # Asserted on the outcome rather than the response. A clean move returns "ok" as a bare
        # string; the JSON payload with a status field only appears when the move has something
        # structured to report, such as rewritten referencers or a refusal.
        file.write("TestPolicy/.gitignore", "*.log\n")

        explorer.move("TestPolicy/.gitignore", "TestPolicy/moved.gitignore")
        assert not _resource_exists(file, "TestPolicy/.gitignore")
        assert _resource_exists(file, "TestPolicy/moved.gitignore")

        explorer.delete("TestPolicy/moved.gitignore")
        assert not _resource_exists(file, "TestPolicy/moved.gitignore")


class TestResourcePolicyNoVisibilityGate:
    """Creating a resource is gated by write access alone.

    Celbridge 1.0 ships no configurable access policy, so a destination is
    refused only by the system tier or a read-only root. A name that a previous
    release would have refused for being invisible now creates normally.
    """

    def test_create_temp_file_allowed(self, explorer, file):
        explorer.create_file("TestPolicy/scratch.tmp")
        names = [i["name"] for i in file.list_contents("TestPolicy")]
        assert "scratch.tmp" in names

    def test_write_backup_file_allowed(self, file):
        file.write("TestPolicy/notes.bak", "content")
        assert _resource_exists(file, "TestPolicy/notes.bak")

    def test_move_to_any_destination_allowed(self, explorer, file):
        explorer.create_file("TestPolicy/keep.txt")
        explorer.move("TestPolicy/keep.txt", "TestPolicy/keep.tmp")
        names = [i["name"] for i in file.list_contents("TestPolicy")]
        assert "keep.txt" not in names
        assert "keep.tmp" in names
