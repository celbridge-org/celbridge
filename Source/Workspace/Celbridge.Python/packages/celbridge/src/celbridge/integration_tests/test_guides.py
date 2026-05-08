class TestGuides:

    # guides_list

    def test_list_returns_index_with_kinds(self, guides):
        result = guides.list()
        assert "guides" in result
        entries = result["guides"]
        assert len(entries) > 0
        kinds = {entry["kind"] for entry in entries}
        assert "concept" in kinds

    def test_list_orders_concepts_before_tools(self, guides):
        entries = guides.list()["guides"]
        last_concept = -1
        first_tool = len(entries)
        for index, entry in enumerate(entries):
            if entry["kind"] == "concept":
                last_concept = index
            elif entry["kind"] == "tool" and first_tool == len(entries):
                first_tool = index
        if first_tool < len(entries):
            assert last_concept < first_tool

    def test_list_starts_with_agent_instructions(self, guides):
        entries = guides.list()["guides"]
        assert entries[0]["name"] == "agent_instructions"
        assert entries[0]["kind"] == "concept"

    def test_list_includes_resource_keys_guide(self, guides):
        names = {entry["name"] for entry in guides.list()["guides"]}
        assert "resource_keys" in names

    def test_list_is_idempotent(self, guides):
        first = guides.list()
        second = guides.list()
        assert first == second

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
        # file_grep is a real MCP tool; even without a per-tool guide it should
        # resolve to a stub entry carrying the language invocation strings.
        result = guides.read('["file_grep"]')
        assert len(result["results"]) == 1
        entry = result["results"][0]
        assert entry["kind"] == "tool"
        assert entry["pythonInvocation"].startswith("cel.file.grep(")
        assert entry["javaScriptInvocation"].startswith("cel.file.grep(")

    # guides_search

    def test_search_finds_known_guide(self, guides):
        result = guides.search("resource keys")
        assert result["totalMatches"] >= 1
        names = [match["name"] for match in result["matches"]]
        assert "resource_keys" in names

    def test_search_caps_results_at_limit(self, guides):
        result = guides.search("the", limit=5)
        assert len(result["matches"]) <= 5
        assert result["totalMatches"] >= len(result["matches"])

    def test_search_clamps_limit_above_max(self, guides):
        # The cap is 25; passing a larger limit must not blow up the response.
        result = guides.search("a", limit=500)
        assert len(result["matches"]) <= 25

    def test_search_returns_error_on_invalid_regex(self, guides):
        result = guides.search("[unclosed")
        assert result["matches"] == []
        assert result["error"]
