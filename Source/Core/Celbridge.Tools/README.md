# Celbridge.Tools — maintainer guide

This project hosts the MCP tool surface that agents and the Python / JavaScript proxies call. The conventions below are what makes the tool surface coherent — most are not self-evident from the code alone. Read this before adding a new tool, a new namespace, or changing how tool errors flow.

The design rationale lives in `05_development/02_proposals/tool_surface_redesign.md` (and its phase history). This file is the operational summary for future maintainers.

## Two summaries per tool

Every tool carries **two** summaries that serve different purposes. Keeping them distinct is intentional.

- **XML `<summary>` on the tool method** — the *discriminator*. Read by `tools/list`, which is paid for in every agent context. One short sentence that helps an agent decide whether to **pick** this tool over other candidates. Not full documentation.
- **`description:` in the per-tool guide frontmatter** — the *application summary*. Read by `guides_list` and surfaced to agents looking up a tool they've already chosen. Describes **how** the tool is used. Distinct from the discriminator.

The body of the per-tool guide carries the parameter semantics, gotchas, examples, and cross-references — anything that would bloat the discriminator. Agents reach the body via `guides_read`, which is on-demand.

If you find yourself wanting to make the XML summary longer to "fully document" the tool, write the full content in the per-tool guide instead. The XML summary stays terse.

## The discriminator-only XML rule

The XML doc for a tool method contains **only** the discriminator and parameter descriptions (and `<returns>` for tools that return a value, since MCP has no output schema). Don't write multi-paragraph `<summary>` blocks, don't link to other tools, don't embed examples, don't restate concept-guide content. Anything beyond the discriminator belongs in the per-tool guide.

The bootstrap tools (`guides_list`, `guides_read`, `guides_search`) are the exception — their XML summaries stay informative because cold-start agents read them through `tools/list` before they know any guides exist.

## `READ GUIDE FIRST.` directive

Tools whose misuse can cause silent data loss or write the wrong thing prepend `READ GUIDE FIRST.` to their XML summary. The directive is short, blunt, and consistent across destructive tools so an agent learns to recognise it. Examples: edit and write tools in `file`, mutating tools in `spreadsheet`. Read-only tools and idempotent tools omit it.

## The cold-start gate

`Celbridge.Server.Services.AgentGate` registers an MCP `CallToolFilter` that blocks any non-bootstrap tool call from a connection that hasn't read the orientation guide on its session. The gate keys on per-connection state held by `AgentTelemetry` and is identified by the `clientInfo.name` reported during MCP `initialize`.

Bootstrap tools (`guides_list`, `guides_read`, `guides_search`) bypass the gate.

Proxy connections — those identified by `clientInfo.name == "CelbridgeMcpToolBridge"` — bypass the gate entirely. The Python and JavaScript proxies are scripted callers that don't need orientation. The `ProxyClientName` constant on `AgentTelemetry` is the single source of truth for that name.

Workspace switches restart the Kestrel instance and `ServerService.StopAsync` calls `AgentTelemetry.ClearSessions` to drop the per-session map, resetting all gate state. Project reloads (no broker restart) do not reset.

## The privileged role of `agent_instructions`

`agent_instructions` is the orientation guide. It is the only guide whose name is hard-coded in three places:

- `AppTools.GetState.AgentDocsPointerValue` — the `agentDocs.entry` field of `app_get_state`.
- `AgentTelemetry.MarkGuideRead` — flips `OrientationRead` when a `guides_read` call resolves this name.
- `AgentGate.BuildOrientationGateError` — the unlock command surfaced in the gate error.

If you rename it again, all three sites need to change.

## Three guide kinds

Guides live under `Guides/` in three folders, each loaded as a different `GuideKind`:

- **`Concepts/` — `GuideKind.Concept`.** Cross-cutting topics: resource keys, the file save model, regex syntax, command conventions. Concept guides may set a `priority:` in frontmatter; lower priority sorts earlier in `guides_list`.
- **`Namespaces/` — `GuideKind.Namespace`.** One per registered MCP namespace (`app`, `document`, `explorer`, `file`, `guides`, `package`, `spreadsheet`, `webview`). Consolidates the must-knows for the namespace and lists the tools as they group.
- **`Tools/` — `GuideKind.Tool`.** One per registered MCP tool. Body covers parameters, return shape, gotchas, examples.

The three kinds are mutually exclusive; a name cannot collide across folders.

## Bidirectional load-time validators

`Celbridge.Tools.Guides.Load` runs at app startup and hard-fails if any of these invariants break:

- Every file under `Concepts/`, `Namespaces/`, or `Tools/` parses as a valid frontmatter+body markdown file with a `name:` matching the filename stem.
- Every per-tool guide name matches a registered MCP tool alias.
- Every namespace guide name matches a registered MCP namespace.
- Every registered MCP tool has a per-tool guide.
- Every registered MCP namespace has a namespace guide.
- No name collisions across the three folders, and namespace / concept names don't collide with tool aliases.

If you add a tool but forget the guide, the app won't launch — the build doesn't catch this; only startup does.

## Adding a new tool — checklist

1. Implement the partial method in `Tools/<Namespace>/<NamespaceTools>.<Method>.cs`. Follow the existing partial-class layout. Use `[McpServerTool(Name = "<namespace>_<method>")]` and `[ToolAlias("<namespace>.<method>")]`.
2. Write the XML `<summary>` as a discriminator only. Add `READ GUIDE FIRST.` at the start if the tool is destructive. Document parameters with `<param>` and add a `<returns>` if the tool returns a value.
3. Author the per-tool guide at `Guides/Tools/<namespace>_<method>.md` using `Guides/template_guide.md` as a starting scaffold. Match `name:` to the tool alias.
4. If the tool has agent-recoverable failure modes, return `ToolError(...)` for them — the suffix and length cap come for free. Bootstrap tools use `BootstrapToolError(...)` instead.
5. Add a unit test in `Source/Tests/Tools/<Namespace>ToolTests.cs` covering the happy case and the most common failure mode. Add a Python integration test in `Source/Workspace/Celbridge.Python/packages/celbridge/src/celbridge/integration_tests/test_<namespace>.py` for end-to-end coverage through the proxy.

## Adding a new namespace — checklist

1. Add the tool source files under `Tools/<Namespace>/`. The namespace name is the part before the first dot in `[ToolAlias("<namespace>.<method>")]`.
2. Author the namespace guide at `Guides/Namespaces/<namespace>.md`. Match `name:` to the namespace name.
3. Extend `agent_instructions` to cite the new namespace in the "Domain prep — namespace guides" section so agents know to read it before working in the new domain.
4. Add per-tool guides for every tool in the namespace (the load-time validator hard-fails if any are missing).

## Telemetry and the agent report

`AgentTelemetry` captures every tool invocation as a `ToolInvocationRecord` row (timestamp, session id, client name and version, tool name, success, duration, payload sizes, proxy and cache-miss flags). The rows are the source of truth.

`cel.agent.get_report()` writes a consolidated agent report as an `.xlsx` workbook to the project root via ClosedXML. Four sheets:

- **Summary** — generated timestamp, registered-tool counts, payload totals, invocation totals, sessions, error counts, agent cache misses.
- **Tools** — one row per registered tool, joining payload size with telemetry: chars, approx tokens, calls, errors, error rate, agent cache misses, avg duration. The pivot point: sort by tokens to find expensive tools, by calls to find hot tools, or compare both columns to spot tools paying context cost without earning their keep. Tools that exist but were never called still appear with zero call metrics.
- **Namespaces** — per-namespace aggregates of the above.
- **Invocations** — every captured call. The substrate for ad-hoc analysis (pivot tables, charts, slicing by session or client).

The workbook is the only output format. If you need a quick eyeball view, open Summary; if you want the at-a-glance pivot, open Tools; if you need to dig in, work from Invocations. `AgentReportBuilder` does the aggregation and report build; `AgentReportBuilderRpcHandler` exposes it over the broker's TCP RPC channel as `diagnostics/get_agent_report`. New analytics methods (top-N queries, per-session breakdowns, cost projections) belong on `AgentReportBuilder` so they share the same payload+telemetry join.

## See also

- `tool_surface_redesign.md` (under `05_development/02_proposals/` then `02_working/` then `03_landed/`) — the design rationale and phase history for everything described above.
- `Guides/template_guide.md` — the authoring scaffold for new per-tool guides.
- `Tools/AgentToolBase.cs` — `ToolError`, `BootstrapToolError`, and the helpers every tool uses.
- `Server/Services/AgentGate.cs`, `AgentTelemetry.cs` — the cold-start gate and telemetry infrastructure.
