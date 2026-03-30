"""Type mapping and signature building for MCP tool descriptors.

Converts JSON Schema types from the McpToolBridge to Python type names
and builds human-readable signatures for help() and error messages.
"""

import inspect
import re


# Map JSON Schema type names to Python type names for display
_JSON_SCHEMA_TO_PYTHON = {
    "string": "str",
    "boolean": "bool",
    "integer": "int",
    "number": "float",
    "array": "list",
    "object": "dict",
}

# Map Python type name strings to Python type objects for inspect.Parameter annotations
_PYTHON_TYPE_MAP = {
    "str": str,
    "int": int,
    "bool": bool,
    "float": float,
}


def to_python_type(json_schema_type: str) -> str:
    """Convert a JSON Schema type name to a Python type name for display."""
    return _JSON_SCHEMA_TO_PYTHON.get(json_schema_type, json_schema_type)


def snake_to_camel(name: str) -> str:
    """Convert a snake_case parameter name to camelCase for JSON-RPC.

    Examples:
        "file_resource" -> "fileResource"
        "force_reload"  -> "forceReload"
        "name"          -> "name"
    """
    parts = name.split("_")
    return parts[0] + "".join(word.capitalize() for word in parts[1:])


def camel_to_snake(name: str) -> str:
    """Convert a camelCase parameter name to snake_case for Python.

    Examples:
        "fileResource" -> "file_resource"
        "forceReload"  -> "force_reload"
        "name"         -> "name"
    """
    result = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", name)
    return result.lower()


def build_signature(tool: dict) -> str:
    """Build a Python-style type-annotated signature string from a tool descriptor.

    Returns something like: (file_resource: str, force_reload: bool = False) -> str
    """
    parts = []
    for parameter in tool.get("parameters", []):
        parameter_name = camel_to_snake(parameter["name"])
        parameter_type = to_python_type(parameter.get("type", ""))
        type_annotation = f": {parameter_type}" if parameter_type else ""
        has_default = parameter.get("hasDefaultValue", False)
        if has_default:
            default_value = parameter.get("defaultValue")
            parts.append(f"{parameter_name}{type_annotation} = {default_value!r}")
        else:
            parts.append(f"{parameter_name}{type_annotation}")

    signature = "(" + ", ".join(parts) + ")"

    return_type = to_python_type(tool.get("returnType", ""))
    if return_type:
        signature += f" -> {return_type}"

    return signature


def build_docstring(tool: dict) -> str:
    """Build a docstring from a tool descriptor including parameter descriptions."""
    lines = []
    description = tool.get("description", "")
    if description:
        lines.append(description)

    parameters = tool.get("parameters", [])
    if parameters:
        lines.append("")
        lines.append("Args:")
        for parameter in parameters:
            parameter_name = camel_to_snake(parameter["name"])
            parameter_description = parameter.get("description", "")
            parameter_type = to_python_type(parameter.get("type", ""))
            type_hint = f" ({parameter_type})" if parameter_type else ""
            lines.append(f"    {parameter_name}{type_hint}: {parameter_description}")

    return "\n".join(lines)


def build_inspect_signature(tool: dict) -> inspect.Signature:
    """Build an inspect.Signature from a tool descriptor.

    This gives help() proper parameter names, types, and defaults
    instead of showing (*args, **kwargs).
    """
    parameters = []
    for param in tool.get("parameters", []):
        parameter_name = camel_to_snake(param["name"])
        python_type_name = to_python_type(param.get("type", ""))
        annotation = _PYTHON_TYPE_MAP.get(python_type_name, inspect.Parameter.empty)
        has_default = param.get("hasDefaultValue", False)
        default = param.get("defaultValue") if has_default else inspect.Parameter.empty

        parameters.append(inspect.Parameter(
            parameter_name,
            kind=inspect.Parameter.POSITIONAL_OR_KEYWORD,
            default=default,
            annotation=annotation,
        ))

    python_return_type = to_python_type(tool.get("returnType", ""))
    return_annotation = _PYTHON_TYPE_MAP.get(python_return_type, inspect.Parameter.empty)

    return inspect.Signature(parameters, return_annotation=return_annotation)


def partition_tools_by_namespace(tools: list[dict]) -> tuple[list[dict], dict[str, list[dict]]]:
    """Partition a list of tools into top-level tools and namespace-grouped tools.

    Returns a tuple of (top_level_tools, namespaced_tools) where namespaced_tools
    is a dict mapping namespace name to the list of tools in that namespace.
    """
    top_level_tools: list[dict] = []
    namespaced_tools: dict[str, list[dict]] = {}

    for tool in tools:
        alias = tool.get("alias", "")
        if not alias:
            continue
        if "." in alias:
            namespace_name = alias.split(".", 1)[0]
            if namespace_name not in namespaced_tools:
                namespaced_tools[namespace_name] = []
            namespaced_tools[namespace_name].append(tool)
        else:
            top_level_tools.append(tool)

    return top_level_tools, namespaced_tools


def format_namespace_doc(namespace_name: str, tools: list[dict]) -> str:
    """Format a help doc section for a single namespace and its tools."""
    lines = [f"cel.{namespace_name}"]
    for tool in sorted(tools, key=lambda t: t.get("alias", "")):
        alias = tool.get("alias", "")
        method_name = alias.split(".", 1)[1]
        signature = build_signature(tool)
        description = tool.get("description", "")
        lines.append(f"    .{method_name}{signature}")
        if description:
            lines.append(f"        {description}")
        lines.append("")

    return "\n".join(lines)
