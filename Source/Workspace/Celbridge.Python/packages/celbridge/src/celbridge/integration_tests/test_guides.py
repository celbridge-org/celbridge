class TestGuides:

    # guides_read

    def test_read_concept_guide(self, guides):
        result = guides.read('["resource_keys"]')
        results = result["results"]
        assert len(results) == 1
        entry = results[0]
        assert entry["name"] == "resource_keys"
        assert entry["kind"] == "concept"
        assert "Resource keys" in entry["body"] or "resource keys" in entry["body"].lower()
        assert result["unknown"] == []

    def test_read_unknown_name_lands_in_unknown(self, guides):
        result = guides.read('["definitely_not_a_real_guide"]')
        assert result["results"] == []
        assert result["unknown"] == ["definitely_not_a_real_guide"]

    def test_read_partial_resolution(self, guides):
        result = guides.read('["resource_keys", "nonexistent"]')
        names = [entry["name"] for entry in result["results"]]
        assert "resource_keys" in names
        assert result["unknown"] == ["nonexistent"]

    def test_read_tool_alias_returns_invocations(self, guides):
        # The Guides loader enforces a per-tool guide for every registered
        # MCP tool, and tool entries carry the language-specific invocation
        # strings alongside the body so the agent can call straight from
        # the response.
        result = guides.read('["file_grep"]')
        assert len(result["results"]) == 1
        entry = result["results"][0]
        assert entry["kind"] == "tool"
        assert entry["pythonInvocation"].startswith("cel.file.grep(")
        assert entry["javaScriptInvocation"].startswith("cel.file.grep(")
