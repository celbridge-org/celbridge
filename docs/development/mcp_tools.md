# MCP Tools

MCP tool classes in `Celbridge.Tools` use the MCP SDK's `XmlToDescriptionGenerator` source generator, which converts XML doc comments into `[Description]` attributes at build time. This means:

- Tool classes must be `partial class` and tool methods must be `partial`
- Use `/// <summary>` for tool descriptions (not `[Description]` attributes)
- The `<summary>` is a **discriminator**, not documentation: one short sentence (~100 chars) that helps an agent decide whether to **pick** this tool over other candidates. Do not add `<param>` or `<returns>` tags, do not write multi-paragraph blocks. Parameter semantics, return shape, gotchas, examples, and cross-references all go in the per-tool guide under `Source/Core/Celbridge.Tools/Guides/Tools/<tool_name>.md`
- Do not add `using System.ComponentModel`
