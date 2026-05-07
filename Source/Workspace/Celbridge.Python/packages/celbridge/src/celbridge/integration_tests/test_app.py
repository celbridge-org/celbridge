class TestApp:

    def test_get_status(self, app):
        result = app.get_status()
        assert result["isLoaded"]
        assert len(result["projectName"]) > 0

    def test_get_status_returns_agent_docs_pointer(self, app):
        result = app.get_status()
        agent_docs = result["agentDocs"]
        assert agent_docs["entry"] == "getting_started"
        assert agent_docs["via"] == "docs_read"

    def test_get_version(self, app):
        version = app.get_version()
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
