import pytest

from celbridge.cel_proxy import CelError

from .helpers import close_if_open, delete_if_exists


@pytest.fixture(autouse=True)
def workspace(explorer, file, document):
    delete_if_exists(explorer, "TestDocument")
    explorer.create_folder("TestDocument")
    file.write(
        "TestDocument/hello.txt",
        "Hello, World!\nLine 2\nLine 3\nLine 4\nLine 5\n",
    )
    yield
    close_if_open(document, "TestDocument/hello.txt")
    close_if_open(document, "TestDocument/new_file.txt")
    delete_if_exists(explorer, "TestDocument")


class TestDocument:

    def test_open_and_activate(self, document):
        document.open("TestDocument/hello.txt")
        document.activate("TestDocument/hello.txt")

    def test_get_state(self, document):
        document.open("TestDocument/hello.txt")
        ctx = document.get_state()
        assert "activeDocument" in ctx
        assert "openDocuments" in ctx
        assert "sectionCount" in ctx
        resources = [d["resource"] for d in ctx["openDocuments"]]
        assert "TestDocument/hello.txt" in resources

    def test_close(self, document):
        document.open("TestDocument/hello.txt")
        document.close("TestDocument/hello.txt", force_close=True)
        ctx = document.get_state()
        resources = [d["resource"] for d in ctx["openDocuments"]]
        assert "TestDocument/hello.txt" not in resources

    def test_close_multiple_documents(self, document, file):
        file.write("TestDocument/new_file.txt", "temp")
        document.open("TestDocument/hello.txt")
        document.open("TestDocument/new_file.txt")
        document.close(
            ["TestDocument/hello.txt", "TestDocument/new_file.txt"],
            force_close=True,
        )
        ctx = document.get_state()
        resources = [d["resource"] for d in ctx["openDocuments"]]
        assert "TestDocument/hello.txt" not in resources
        assert "TestDocument/new_file.txt" not in resources

    def test_open_invalid_resource_key(self, document):
        with pytest.raises(CelError):
            document.open("\\invalid")

    def test_open_invalid_section_index(self, document):
        with pytest.raises(CelError):
            document.open("TestDocument/hello.txt", section_index=5)

    def test_activate_invalid_resource_key(self, document):
        with pytest.raises(CelError):
            document.activate("\\invalid")
