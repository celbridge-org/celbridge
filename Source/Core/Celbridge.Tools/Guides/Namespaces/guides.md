---
name: guides
description: Discover and read the built-in agent guide library — orientation, namespace overviews, conceptual guides, and per-tool guides.
---

# guides

The `guides` namespace exposes the built-in agent guide library: the meta-documentation describing how to work in this application. Guides come in three kinds: **concept** (cross-cutting topics like resource keys or the file save model), **namespace** (per-namespace overviews like this one), and **tool** (one per registered MCP tool, with full parameter and return semantics). The library is loaded once at application startup from embedded markdown.

## Must-knows

- **`guides_*` are the bootstrap tools.** They are the only tools an agent can call before the orientation guide has been read on a fresh session. Use them to satisfy the cold-start gate by calling `guides_read(["agent_instructions"])`.
- **Per-tool guides carry runnable invocation strings.** Each tool entry returned by `guides_read` includes `pythonInvocation` and `javaScriptInvocation` strings derived from the tool's reflected signature.
- **Names are exact.** `guides_read` resolves names against frontmatter `name` fields (matching tool aliases for tool guides, namespace names for namespace guides). Misses go to the response's `unknown` array rather than failing.
- **Pair `guides_list` with `guides_search`.** `guides_list` enumerates everything in canonical order (concepts, namespaces, tools); `guides_search` runs a regex over names, descriptions, and bodies for keyword discovery.

## Tools

- `guides_list` — enumerate every guide in canonical order with one-line descriptions.
- `guides_read` — read one or more guides by name. Tool entries carry Python and JavaScript invocation strings.
- `guides_search` — regex search over guide names, descriptions, and bodies, ranked by score.

## See also

- `agent_instructions` — the orientation guide every agent reads on a fresh session.
- `regex_syntax` — supported constructs for `guides_search`.
