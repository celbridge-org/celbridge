# Celbridge.Tools — maintainer guide

This project hosts the MCP tool surface that agents and the Python / JavaScript proxies call. The conventions below are what makes the tool surface coherent — most are not self-evident from the code alone. Read this before adding a new tool, a new namespace, or changing how tool errors flow.

The design rationale lives in `05_development/02_proposals/tool_surface_redesign.md` and `tool_guide_auto_attach.md` (and their phase histories). This file is the operational summary for future maintainers.

## The discriminator-only XML rule

The XML doc for a tool method contains **only** a single-line `<summary>` — one short sentence (~100 chars) that helps an agent decide whether to **pick** this tool over other candidates. No `<param>` tags, no `<returns>` tags, no multi-paragraph blocks, no links to other tools, no embedded examples, no restated concept-guide content. Anything beyond the discriminator belongs in the per-tool guide.

The body of the per-tool guide carries the parameter semantics, gotchas, examples, and cross-references. The agent reaches the body via auto-attach on first use, or via `guides_read` on demand.

## Auto-attach on first use

`Celbridge.Server.Services.AgentResponseFilter` registers an MCP `CallToolFilter` that inlines guide bodies into tool responses on first use, per session. Three guides are eligible to attach on each non-proxy call: the orientation guide (`agent_instructions`), the tool's namespace guide, and the tool's per-tool guide. Each attaches exactly once per MCP session via `AgentMonitor.TryMarkServed`; subsequent calls return the bare result. When any guides attach, the guide blocks come before the original result content.

Proxy connections — those identified by `clientInfo.name == "CelbridgeMcpToolBridge"` — bypass auto-attach entirely. The Python and JavaScript proxies are scripted callers that don't need guide bodies on every script call. The `ProxyClientName` constant on `AgentMonitor` is the single source of truth for that name.

Workspace switches restart the Kestrel instance and `ServerService.StopAsync` calls `AgentMonitor.ClearSessions` to drop the per-session map, resetting all auto-attach state. Project reloads (no broker restart) do not reset.

## The privileged role of `agent_instructions`

`agent_instructions` is the orientation guide. Its name is hard-coded in two places:

- `AppTools.GetState.AgentDocsPointerValue` — the `agentDocs.entry` field of `app_get_state`.
- `AgentResponseFilter.ApplyAutoAttach` — the orientation guide's name in the auto-attach lookup.

If you rename it again, both sites need to change.

## Three guide kinds

Guides live under `Guides/` in three folders, each loaded as a different `GuideKind`:

- **`Concepts/` — `GuideKind.Concept`.** Cross-cutting topics: resource keys, the file save model, regex syntax, command conventions.
- **`Namespaces/` — `GuideKind.Namespace`.** One per registered MCP namespace (`app`, `document`, `explorer`, `file`, `guides`, `package`, `spreadsheet`, `webview`). Consolidates the must-knows for the namespace and lists the tools as they group.
- **`Tools/` — `GuideKind.Tool`.** One per registered MCP tool. Body covers parameters, return shape, gotchas, examples.

The three kinds are mutually exclusive; a name cannot collide across folders. Guide files are plain markdown — no YAML frontmatter. The first line must be a `#` heading whose text matches the tool, namespace, or concept name.

## Bidirectional load-time validators

`Celbridge.Tools.Guides.Load` runs at app startup and hard-fails if any of these invariants break:

- Every guide resource is under `Concepts/`, `Namespaces/`, or `Tools/`, with a filename stem free of dots.
- Every per-tool guide name matches a registered MCP tool alias.
- Every namespace guide name matches a registered MCP namespace.
- Every registered MCP tool has a per-tool guide.
- Every registered MCP namespace has a namespace guide.
- No name collisions across the three folders, and namespace / concept names don't collide with tool aliases.

If you add a tool but forget the guide, the app won't launch — the build doesn't catch this; only startup does.

## Adding a new tool — checklist

1. Implement the partial method in `Tools/<Namespace>/<NamespaceTools>.<Method>.cs`. Follow the existing partial-class layout. Use `[McpServerTool(Name = "<namespace>_<method>")]` and `[ToolAlias("<namespace>.<method>")]`.
2. Write the XML `<summary>` as a discriminator only — one sentence under ~100 chars. No `<param>` or `<returns>` tags; full parameter and return semantics go in the per-tool guide.
3. Author the per-tool guide at `Guides/Tools/<namespace>_<method>.md` using `Guides/template_guide.md` as a starting scaffold. Match the filename stem to the tool alias and open the body with a `# <namespace>_<method>` heading.
4. If the tool has agent-recoverable failure modes, return `ToolResponse.Error(...)` for them — the length cap and `IsError` flag come for free. Use the category helpers (`InvalidResourceKey`, `FeatureFlagDisabled`, `ResourceNotFound`) when they fit.
5. Add a unit test in `Source/Tests/Tools/<Namespace>ToolTests.cs` covering the happy case and the most common failure mode. Add a Python integration test in `Source/Workspace/Celbridge.Python/packages/celbridge/src/celbridge/integration_tests/test_<namespace>.py` for end-to-end coverage through the proxy.

## Adding a new namespace — checklist

1. Add the tool source files under `Tools/<Namespace>/`. The namespace name is the part before the first dot in `[ToolAlias("<namespace>.<method>")]`.
2. Author the namespace guide at `Guides/Namespaces/<namespace>.md`. The filename stem must match the namespace name; open the body with a `# <namespace>` heading.
3. Extend `agent_instructions` to cite the new namespace in the "Domain prep — namespace guides" section so agents know to read it before working in the new domain.
4. Add per-tool guides for every tool in the namespace (the load-time validator hard-fails if any are missing).

## Monitoring and the agent report

`AgentMonitor` captures every tool invocation as a `ToolInvocationRecord` row (timestamp, session id, client name and version, tool name, success, duration, payload sizes, proxy and cache-miss flags). The rows are the source of truth.

`cel.agent.get_report()` writes a consolidated agent report as an `.xlsx` workbook to the project root via ClosedXML. Four sheets:

- **Summary** — generated timestamp, registered-tool counts, payload totals, invocation totals, sessions, error counts, agent cache misses.
- **Tools** — one row per registered tool, joining payload size with monitoring data: chars, approx tokens, calls, errors, error rate, agent cache misses, avg duration. The pivot point: sort by tokens to find expensive tools, by calls to find hot tools, or compare both columns to spot tools paying context cost without earning their keep. Tools that exist but were never called still appear with zero call metrics.
- **Namespaces** — per-namespace aggregates of the above.
- **Invocations** — every captured call. The substrate for ad-hoc analysis (pivot tables, charts, slicing by session or client).

The workbook is the only output format. If you need a quick eyeball view, open Summary; if you want the at-a-glance pivot, open Tools; if you need to dig in, work from Invocations. `AgentReportBuilder` does the aggregation and report build; `AgentReportBuilderRpcHandler` exposes it over the broker's TCP RPC channel as `diagnostics/get_agent_report`. New analytics methods (top-N queries, per-session breakdowns, cost projections) belong on `AgentReportBuilder` so they share the same payload+monitoring join.

## See also

- `tool_surface_redesign.md` and `tool_guide_auto_attach.md` (under `05_development/02_proposals/` then `02_working/` then `03_landed/`) — the design rationale and phase history for everything described above.
- `Guides/template_guide.md` — the authoring scaffold for new per-tool guides.
- `Tools/ToolResponse.cs` — `Error`, `Success`, `SuccessWithImage`, and the category helpers (`InvalidResourceKey`, `FeatureFlagDisabled`, `ResourceNotFound`) every tool uses. `Tools/AgentToolBase.cs` provides the DI plumbing those tools share.
- `Server/Services/AgentResponseFilter.cs`, `AgentMonitor.cs` — the auto-attach filter and per-invocation monitoring infrastructure.
