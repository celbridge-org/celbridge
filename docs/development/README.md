# Development Documentation

Documentation for people working on Celbridge itself. For using the application, see
[Getting Started](../getting_started.md).

| Document | Covers |
|---|---|
| [Building and Testing](building.md) | Building on Windows and macOS, running the .NET, JavaScript and Python test suites, linting, and CI |
| [Coding Conventions](coding_conventions.md) | Conventions for C#, JavaScript and Python, plus the general rules that apply to all three |
| [Architecture](architecture.md) | The solution layout, service lifetimes and dependency injection rules, the command system, the `Platform/` folder convention, feature flags, and the document save model |
| [Design Tokens](design_tokens.md) | The generated colour and dimension tokens shared by the XAML and web sides |
| [Console Layout](console_layout.md) | How a console's terminal, pty and WebView agree on a size on each head, the failures that has caused and the rules that prevent them, and how to investigate a new one |
| [MCP Tools](mcp_tools.md) | Authoring MCP tool classes in `Celbridge.Tools` |
| [Agent Tests](agent_tests/README.md) | Test plans an agent runs against the running app, for behaviour CI cannot reach; one per area, each with three effort levels |
| [Agent Guides](../../Source/Core/Celbridge.Tools/Guides/README.md) | Authoring the embedded markdown guides the MCP broker prepends to tool responses |
| [Report Producers](../../Source/Core/Celbridge.Utilities/Services/README.md) | Writing a report producer: whether an operation deserves a report, which findings to declare, and where the report goes |
