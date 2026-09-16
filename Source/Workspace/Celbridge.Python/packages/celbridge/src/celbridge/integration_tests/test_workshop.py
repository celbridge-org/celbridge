import pytest

from celbridge.cel_proxy import CelError

from .helpers import delete_if_exists

INTEGRATION_PACKAGE_NAME = "test-integration-pkg"
INTEGRATION_PACKAGE_FOLDER = f"packages/{INTEGRATION_PACKAGE_NAME}"
INTEGRATION_PACKAGE_MANIFEST = f"""[package]
name = "{INTEGRATION_PACKAGE_NAME}"
title = "Test Integration Package"
"""

INTEGRATION_PAGE_FOLDER = "pages/test-integration-page"
INTEGRATION_PAGE_PATH = "celbridge-integration-tests/page"
INTEGRATION_PAGE_MANIFEST = f"""[publish]
path = "{INTEGRATION_PAGE_PATH}"
"""


@pytest.fixture(autouse=True)
def workspace(explorer):
    delete_if_exists(explorer, INTEGRATION_PACKAGE_FOLDER)
    delete_if_exists(explorer, INTEGRATION_PAGE_FOLDER)
    yield
    delete_if_exists(explorer, INTEGRATION_PACKAGE_FOLDER)
    delete_if_exists(explorer, INTEGRATION_PAGE_FOLDER)


def _build_integration_package(explorer, file):
    explorer.create_folder(INTEGRATION_PACKAGE_FOLDER)
    file.write(f"{INTEGRATION_PACKAGE_FOLDER}/package.toml", INTEGRATION_PACKAGE_MANIFEST)
    file.write(f"{INTEGRATION_PACKAGE_FOLDER}/data.txt", "integration round-trip payload\n")


def _build_integration_page(explorer, file):
    explorer.create_folder(INTEGRATION_PAGE_FOLDER)
    file.write(f"{INTEGRATION_PAGE_FOLDER}/pages.toml", INTEGRATION_PAGE_MANIFEST)
    file.write(f"{INTEGRATION_PAGE_FOLDER}/index.html", "<!doctype html>\n<title>Integration page</title>\n")


def _drop_integration_package_if_published(app, workshop):
    # Best-effort prep so a previous run that died mid-cleanup does not leave
    # the workshop in a state that fails this run's publish.
    try:
        app.answer_dialog("Confirmation")
        workshop.unpublish_package(INTEGRATION_PACKAGE_NAME)
    except CelError:
        pass


def _drop_integration_page_if_published(workshop):
    # Best-effort prep and cleanup, as for the package, so a page left behind
    # by an earlier run does not outlive the test.
    try:
        workshop.unpublish_page(INTEGRATION_PAGE_PATH, confirmWithUser=False)
    except CelError:
        pass


class TestWorkshop:

    def test_list_packages(self, workshop):
        result = workshop.list_packages()
        assert isinstance(result, list)
        # Tolerant of an empty workshop, but every present entry must carry
        # the full shape.
        for entry in result:
            assert "packageName" in entry
            assert "latestWorkshopVersion" in entry
            assert "publishedAt" in entry
            assert "workshopVersionCount" in entry
            assert isinstance(entry["packageName"], str)
            assert isinstance(entry["workshopVersionCount"], int)

    def test_publish_install_delete_unpublish_package(self, answer_dialog_available, app, explorer, file, workshop):
        _build_integration_package(explorer, file)
        _drop_integration_package_if_published(app, workshop)

        try:
            publish_result = workshop.publish_package(
                INTEGRATION_PACKAGE_FOLDER,
                summary="integration round-trip publish",
                confirmWithUser=False,
            )
            assert publish_result["packageName"] == INTEGRATION_PACKAGE_NAME
            assert publish_result["workshopVersion"] >= 1
            assert publish_result["entries"] > 0
            published_workshop_version = publish_result["workshopVersion"]

            install_result = workshop.install_package(
                INTEGRATION_PACKAGE_NAME,
                confirmWithUser=False,
            )
            assert install_result["packageName"] == INTEGRATION_PACKAGE_NAME
            assert install_result["workshopVersion"] == published_workshop_version

            app.answer_dialog("Confirmation")
            delete_result = workshop.delete_package(INTEGRATION_PACKAGE_NAME, str(published_workshop_version))
            assert delete_result["packageName"] == INTEGRATION_PACKAGE_NAME
            assert delete_result["workshopVersion"] == published_workshop_version
            assert delete_result["deleted"] is True
        finally:
            # Whether the body succeeded, raised, or the delete failed, we
            # must leave the workshop clean so the next run can publish again.
            _drop_integration_package_if_published(app, workshop)

    def test_install_nonexistent_package(self, workshop):
        with pytest.raises(CelError):
            workshop.install_package("nonexistent-package-xyz-999", confirmWithUser=False)

    def test_install_invalid_package_name(self, workshop):
        with pytest.raises(CelError):
            workshop.install_package("INVALID PACKAGE NAME!", confirmWithUser=False)

    def test_publish_invalid_package_name(self, explorer, file, workshop):
        # A manifest with an invalid name is rejected before any upload.
        explorer.create_folder("packages/invalid-name-source")
        file.write(
            "packages/invalid-name-source/package.toml",
            "[package]\nname = \"INVALID NAME!\"\n",
        )
        try:
            with pytest.raises(CelError):
                workshop.publish_package("packages/invalid-name-source", confirmWithUser=False)
        finally:
            delete_if_exists(explorer, "packages/invalid-name-source")

    def test_publish_nonexistent_source(self, workshop):
        with pytest.raises(CelError):
            workshop.publish_package("NonExistentFolder", confirmWithUser=False)

    def test_list_pages(self, workshop):
        result = workshop.list_pages()
        assert isinstance(result, list)
        # Tolerant of a workshop with no pages, but every present entry must
        # carry the full shape.
        for entry in result:
            assert "path" in entry
            assert "url" in entry
            assert "publishedAt" in entry
            assert "publishedBy" in entry
            assert "contentHash" in entry

    def test_publish_unpublish_page(self, explorer, file, workshop):
        _build_integration_page(explorer, file)
        _drop_integration_page_if_published(workshop)

        try:
            publish_result = workshop.publish_page(INTEGRATION_PAGE_FOLDER, confirmWithUser=False)
            assert publish_result["path"] == INTEGRATION_PAGE_PATH
            assert publish_result["url"]
            assert publish_result["entries"] > 0

            info_result = workshop.get_page_info(INTEGRATION_PAGE_PATH)
            assert info_result["path"] == INTEGRATION_PAGE_PATH

            unpublish_result = workshop.unpublish_page(INTEGRATION_PAGE_PATH, confirmWithUser=False)
            assert unpublish_result["path"] == INTEGRATION_PAGE_PATH
            assert unpublish_result["unpublished"] is True
        finally:
            _drop_integration_page_if_published(workshop)
