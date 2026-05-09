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
        layout_mode = result["layoutMode"]
        assert isinstance(layout_mode["contextPanelVisible"], bool)
        assert isinstance(layout_mode["inspectorPanelVisible"], bool)
        assert isinstance(layout_mode["consolePanelVisible"], bool)
        assert isinstance(layout_mode["consoleMaximized"], bool)

    def test_get_state_returns_version(self, app):
        result = app.get_state()
        version = result["version"]
        parts = version.split(".")
        assert len(parts) == 3, f"Expected 3-part version, got: {version}"

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
