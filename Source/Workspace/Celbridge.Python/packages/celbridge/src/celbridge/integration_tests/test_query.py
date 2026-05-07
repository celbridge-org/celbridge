class TestQuery:

    def test_get_context(self, query):
        ctx = query.get_context()
        assert "Resource Keys" in ctx

    def test_get_python_api(self, query):
        api = query.get_python_api()
        assert "Celbridge Python API Reference" in api
        assert "## document" in api
        assert "## file" in api

    def test_get_python_api_contains_return_types(self, query):
        api = query.get_python_api()
        assert "-> " in api

    def test_get_python_api_contains_parameter_formats(self, query):
        api = query.get_python_api()
        assert "edits_json" in api

    def test_get_context_is_idempotent(self, query):
        first = query.get_context()
        second = query.get_context()
        assert first == second
