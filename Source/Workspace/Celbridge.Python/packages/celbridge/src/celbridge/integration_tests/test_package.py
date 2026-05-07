import pytest

from celbridge.cel_proxy import CelError

from .helpers import delete_if_exists


@pytest.fixture(autouse=True)
def workspace(explorer, file, package):
    delete_if_exists(explorer, "TestPackage")
    delete_if_exists(explorer, "TestPackageExtract")
    delete_if_exists(explorer, "test_archive.zip")
    delete_if_exists(explorer, "test_archive_filtered.zip")
    explorer.create_folder("TestPackage")
    file.write("TestPackage/file.txt", "archive content\n")
    yield
    delete_if_exists(explorer, "TestPackage")
    delete_if_exists(explorer, "TestPackageExtract")
    delete_if_exists(explorer, "test_archive.zip")
    delete_if_exists(explorer, "test_archive_filtered.zip")
    try:
        package.uninstall("test-integration-pkg")
    except Exception:
        pass


class TestPackage:

    def test_archive(self, package):
        result = package.archive("TestPackage", "test_archive.zip", overwrite=True)
        assert result["entries"] > 0
        assert result["size"] > 0

    def test_archive_filtered(self, package):
        result = package.archive(
            "TestPackage",
            "test_archive_filtered.zip",
            include="*.txt",
            overwrite=True,
        )
        assert result["entries"] >= 1

    def test_unarchive(self, package, explorer):
        package.archive("TestPackage", "test_archive.zip", overwrite=True)
        explorer.create_folder("TestPackageExtract")
        result = package.unarchive(
            "test_archive.zip", "TestPackageExtract", overwrite=True
        )
        assert result["entries"] > 0

    def test_list(self, package):
        result = package.list()
        assert isinstance(result, list)

    @pytest.mark.skip(reason="package.uninstall tool not yet implemented; test also needs packages/test-integration-pkg fixture")
    def test_publish_install_uninstall(self, package):
        pub_result = package.publish("TestPackage", "test-integration-pkg")
        assert pub_result["packageName"] == "test-integration-pkg"
        assert pub_result["entries"] > 0

        inst_result = package.install("test-integration-pkg")
        assert inst_result["packageName"] == "test-integration-pkg"

        uninst_result = package.uninstall("test-integration-pkg")
        assert uninst_result["packageName"] == "test-integration-pkg"

    def test_archive_invalid_source(self, package):
        with pytest.raises(CelError):
            package.archive("\\invalid", "test_archive.zip")

    def test_archive_invalid_destination(self, package):
        with pytest.raises(CelError):
            package.archive("TestPackage", "\\invalid")

    def test_unarchive_invalid_archive(self, package):
        with pytest.raises(CelError):
            package.unarchive("\\invalid", "TestPackageExtract")

    def test_install_nonexistent_package(self, package):
        with pytest.raises(CelError):
            package.install("nonexistent-package-xyz-999")

    def test_install_invalid_package_name(self, package):
        with pytest.raises(CelError):
            package.install("INVALID PACKAGE NAME!")

    @pytest.mark.skip(reason="package.uninstall tool not yet implemented")
    def test_uninstall_not_installed(self, package):
        with pytest.raises(CelError):
            package.uninstall("not-installed-package-xyz")

    @pytest.mark.skip(reason="package.uninstall tool not yet implemented")
    def test_uninstall_invalid_package_name(self, package):
        with pytest.raises(CelError):
            package.uninstall("INVALID!")

    def test_publish_invalid_package_name(self, package):
        with pytest.raises(CelError):
            package.publish("TestPackage", "INVALID NAME!")

    def test_publish_nonexistent_source(self, package):
        with pytest.raises(CelError):
            package.publish("NonExistentFolder", "test-pkg")
