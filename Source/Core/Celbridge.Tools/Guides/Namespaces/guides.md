# guides

The `guides` namespace exposes the built-in agent guide library: the meta-documentation describing how to work in this application. Guides come in three kinds: **concept** (cross-cutting topics like resource keys or the file save model), **namespace** (per-namespace overviews like this one), and **tool** (one per registered MCP tool, with full parameter and return semantics). The library is loaded once at application startup from embedded markdown.

## Must-knows

- **Guides arrive automatically.** On the first call into a namespace or to a tool in a session, the relevant per-tool, namespace, and orientation guides ride along in the response. You do not need to fetch them up front.
- **Re-fetch only after a context compaction.** `guides_read` exists for the case where the host context has auto-compacted and a guide that was attached earlier has scrolled out. In a fresh session, prefer to let auto-attach do its job.
- **Tool entries carry runnable invocation strings.** Each tool entry returned by `guides_read` includes `pythonInvocation` and `javaScriptInvocation` strings derived from the tool's reflected signature.
- **Names are exact.** `guides_read` resolves names against the registered MCP tool surface and the orientation guide. Misses go to the response's `unknown` array rather than failing.

## Tools

- `guides_read` — read one or more guides by name. Tool entries carry Python and JavaScript invocation strings.

## See also

- `agent_instructions` — the orientation guide every agent reads on a fresh session (auto-attached on the first non-proxy tool call).
