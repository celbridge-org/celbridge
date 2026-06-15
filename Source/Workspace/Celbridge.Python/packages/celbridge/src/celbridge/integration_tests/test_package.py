import pytest

from celbridge.cel_proxy import CelError

from .helpers import delete_if_exists


INTEGRATION_PACKAGE_NAME = "test-integration-pkg"
INTEGRATION_PACKAGE_FOLDER = f"packages/{INTEGRATION_PACKAGE_NAME}"
# The `author` line is a temporary workaround: the server-side migration
# that drops the manifest `author` requirement (and reads the multipart
# field the client now sends from Workshop settings) has not landed yet,
# so a publish without it 400s on manifest validation. Once the server
# updates per the package_hub_api_migration.md follow-up, this line can
# be removed and the C# loader will keep ignoring a stray `author` key.
INTEGRATION_PACKAGE_MANIFEST = f"""[package]
name = "{INTEGRATION_PACKAGE_NAME}"
title = "Test Integration Package"
author = "Celbridge Integration Tests"
"""


@pytest.fixture(autouse=True)
def workspace(explorer, file):
    delete_if_exists(explorer, "TestPackage")
    delete_if_exists(explorer, "TestPackageExtract")
    delete_if_exists(explorer, "test_archive.zip")
    delete_if_exists(explorer, "test_archive_filtered.zip")
    delete_if_exists(explorer, INTEGRATION_PACKAGE_FOLDER)
    explorer.create_folder("TestPackage")
    file.write("TestPackage/file.txt", "archive content\n")
    yield
    delete_if_exists(explorer, "TestPackage")
    delete_if_exists(explorer, "TestPackageExtract")
    delete_if_exists(explorer, "test_archive.zip")
    delete_if_exists(explorer, "test_archive_filtered.zip")
    delete_if_exists(explorer, INTEGRATION_PACKAGE_FOLDER)


def _build_integration_package(explorer, file):
    explorer.create_folder(INTEGRATION_PACKAGE_FOLDER)
    file.write(f"{INTEGRATION_PACKAGE_FOLDER}/package.toml", INTEGRATION_PACKAGE_MANIFEST)
    file.write(f"{INTEGRATION_PACKAGE_FOLDER}/data.txt", "integration round-trip payload\n")


def _drop_integration_package_if_published(app, package):
    # Best-effort prep so a previous run that died mid-cleanup does not leave
    # the workshop in a state that fails this run's publish.
    try:
        app.answer_dialog("Confirmation")
        package.unpublish(INTEGRATION_PACKAGE_NAME)
    except CelError:
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
        # Each entry follows the v8 package summary shape. Tolerant of an
        # empty workshop, but every present entry must carry the full shape.
        for entry in result:
            assert "packageName" in entry
            assert "latestVersion" in entry
            assert "publishedAt" in entry
            assert "versionsCount" in entry
            assert isinstance(entry["packageName"], str)
            assert isinstance(entry["versionsCount"], int)

    def test_publish_install_unpublish(self, answer_dialog_available, app, explorer, file, package):
        _build_integration_package(explorer, file)
        _drop_integration_package_if_published(app, package)

        try:
            pub_result = package.publish(
                INTEGRATION_PACKAGE_FOLDER,
                summary="integration round-trip publish",
                confirmWithUser=False,
            )
            assert pub_result["packageName"] == INTEGRATION_PACKAGE_NAME
            assert pub_result["version"] >= 1
            assert pub_result["entries"] > 0
            published_version = pub_result["version"]

            inst_result = package.install(
                INTEGRATION_PACKAGE_NAME,
                confirmWithUser=False,
            )
            assert inst_result["packageName"] == INTEGRATION_PACKAGE_NAME
            assert inst_result["version"] == published_version

            app.answer_dialog("Confirmation")
            delete_result = package.delete(INTEGRATION_PACKAGE_NAME, str(published_version))
            assert delete_result["packageName"] == INTEGRATION_PACKAGE_NAME
            assert delete_result["version"] == published_version
            assert delete_result["deleted"] is True
        finally:
            # Whether the body succeeded, raised, or the delete failed, we
            # must leave the workshop clean so the next run can publish again.
            _drop_integration_package_if_published(app, package)

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
            package.install("nonexistent-package-xyz-999", confirmWithUser=False)

    def test_install_invalid_package_name(self, package):
        with pytest.raises(CelError):
            package.install("INVALID PACKAGE NAME!", confirmWithUser=False)

    def test_publish_invalid_package_name(self, explorer, file, package):
        # A manifest with an invalid name is rejected before any upload.
        explorer.create_folder("packages/invalid-name-source")
        file.write(
            "packages/invalid-name-source/package.toml",
            "[package]\nname = \"INVALID NAME!\"\n",
        )
        try:
            with pytest.raises(CelError):
                package.publish("packages/invalid-name-source", confirmWithUser=False)
        finally:
            delete_if_exists(explorer, "packages/invalid-name-source")

    def test_publish_nonexistent_source(self, package):
        with pytest.raises(CelError):
            package.publish("NonExistentFolder", confirmWithUser=False)
