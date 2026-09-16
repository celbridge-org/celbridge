class TestApp:

    def test_get_state(self, app):
        result = app.get_state()
        assert result["isLoaded"]
        assert len(result["projectName"]) > 0

    def test_get_state_returns_focused_panel(self, app):
        result = app.get_state()
        assert isinstance(result["focusedPanel"], str)

    def test_get_state_returns_layout_mode(self, app):
        result = app.get_state()
        area_visibility = result["layoutMode"]["areaVisibility"]
        for area in ["utility", "main", "bottom", "side"]:
            assert isinstance(area_visibility[area], bool), f"Expected a bool for area: {area}"

        # Main is the one area that cannot be hidden.
        assert area_visibility["main"]

    def test_get_state_returns_version(self, app):
        result = app.get_state()
        version = result["version"]
        parts = version.split(".")
        assert len(parts) == 3, f"Expected 3-part version, got: {version}"

    def test_list_packages(self, app):
        result = app.list_packages()
        assert isinstance(result["packages"], list)
        assert isinstance(result["failures"], list)
        for entry in result["packages"]:
            assert isinstance(entry["name"], str)
            assert len(entry["packageVersion"].split(".")) == 3
            assert entry["folder"].startswith("project:")

    def test_get_state_summarizes_the_listed_packages(self, app):
        state = app.get_state()
        listed = app.list_packages()
        summary = [(p["name"], p["packageVersion"]) for p in state["packages"]]
        expected = [(p["name"], p["packageVersion"]) for p in listed["packages"]]
        assert summary == expected
        assert state["packageLoadFailureCount"] == len(listed["failures"])

    def test_log(self, app):
        app.log("Integration test: log message")

    def test_log_warning(self, app):
        app.log_warning("Integration test: warning message")

    def test_log_error(self, app):
        app.log_error("Integration test: error message")

    def test_refresh_files(self, app):
        app.refresh_files()

    def test_log_empty_message(self, app):
        app.log("")

    def test_log_unicode(self, app):
        app.log("Unicode test: éèê 世界 😀")
