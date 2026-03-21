"""Dynamic proxy that exposes Celbridge broker tools via the RPC client.

On construction, queries the broker's tools/list endpoint and generates a
proxy method for each discovered tool. Tools are registered using their alias
which provides short, natural method names. Dotted aliases (e.g. "sheet.delete")
create namespace objects so that `cel.sheet.delete()` works naturally.
"""

import difflib
import inspect
import logging
import re

from celbridge.rpc_client import RpcClient

logger = logging.getLogger(__name__)


def _snake_to_camel(name: str) -> str:
    """Convert a snake_case parameter name to camelCase for JSON-RPC.

    Examples:
        "file_resource" -> "fileResource"
        "force_reload"  -> "forceReload"
        "name"          -> "name"
    """
    parts = name.split("_")
    return parts[0] + "".join(word.capitalize() for word in parts[1:])


def _camel_to_snake(name: str) -> str:
    """Convert a camelCase parameter name to snake_case for Python.

    Examples:
        "fileResource" -> "file_resource"
        "forceReload"  -> "force_reload"
        "name"         -> "name"
    """
    result = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", name)
    return result.lower()


def _build_signature(tool: dict) -> str:
    """Build a Python-style type-annotated signature string from a tool descriptor.

    Returns something like: (file_resource: str, force_reload: bool = False) -> str
    """
    parts = []
    for parameter in tool.get("parameters", []):
        parameter_name = _camel_to_snake(parameter["name"])
        parameter_type = parameter.get("type", "")
        type_annotation = f": {parameter_type}" if parameter_type else ""
        has_default = parameter.get("hasDefaultValue", False)
        if has_default:
            default_value = parameter.get("defaultValue")
            parts.append(f"{parameter_name}{type_annotation} = {default_value!r}")
        else:
            parts.append(f"{parameter_name}{type_annotation}")

    signature = "(" + ", ".join(parts) + ")"

    return_type = tool.get("returnType", "")
    if return_type:
        signature += f" -> {return_type}"

    return signature


def _build_docstring(tool: dict) -> str:
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
            parameter_name = _camel_to_snake(parameter["name"])
            parameter_description = parameter.get("description", "")
            parameter_type = parameter.get("type", "")
            type_hint = f" ({parameter_type})" if parameter_type else ""
            lines.append(f"    {parameter_name}{type_hint}: {parameter_description}")

    return "\n".join(lines)


# Map of simple type name strings to Python type objects for inspect.Parameter annotations
_TYPE_MAP = {
    "str": str,
    "int": int,
    "bool": bool,
    "float": float,
}


def _build_inspect_signature(tool: dict) -> inspect.Signature:
    """Build an inspect.Signature from a tool descriptor.

    This gives help() proper parameter names, types, and defaults
    instead of showing (*args, **kwargs).
    """
    parameters = []
    for param in tool.get("parameters", []):
        parameter_name = _camel_to_snake(param["name"])
        type_name = param.get("type", "")
        annotation = _TYPE_MAP.get(type_name, inspect.Parameter.empty)
        has_default = param.get("hasDefaultValue", False)
        default = param.get("defaultValue") if has_default else inspect.Parameter.empty

        parameters.append(inspect.Parameter(
            parameter_name,
            kind=inspect.Parameter.POSITIONAL_OR_KEYWORD,
            default=default,
            annotation=annotation,
        ))

    return_type_name = tool.get("returnType", "")
    return_annotation = _TYPE_MAP.get(return_type_name, inspect.Signature.empty)

    return inspect.Signature(parameters, return_annotation=return_annotation)


class CelError(Exception):
    """Base exception for cel proxy errors.

    IPython is configured to display these without a traceback,
    showing only the error type and message for a cleaner REPL experience.
    """
    pass


class ToolNamespace:
    """A namespace object for grouping related tool methods under a dotted path.

    For example, cel.sheet is a ToolNamespace that contains cel.sheet.delete().
    """

    def __init__(self, name: str):
        self._namespace_name = name

    def __repr__(self) -> str:
        return f"<ToolNamespace '{self._namespace_name}'>"


class CelProxy:
    """Celbridge application proxy. Type help(cel) to see available commands."""

    def __init__(self, client: RpcClient):
        self._client = client
        self._tools: list[dict] = []
        self._aliases: list[str] = []
        self._discover_tools()

    def _discover_tools(self) -> None:
        """Query the broker for available tools and create proxy methods."""
        try:
            self._tools = self._client.call("tools/list") or []
        except Exception as exception:
            logger.warning("Failed to discover tools from broker: %s", exception)
            self._tools = []
            return

        for tool in self._tools:
            tool_name = tool.get("name", "")
            alias = tool.get("alias", "")

            if not alias:
                logger.warning("Tool '%s' has no alias, skipping", tool_name)
                continue

            proxy = self._make_tool_proxy(tool_name, tool)
            proxy.__doc__ = _build_docstring(tool)

            self._register_proxy(alias, proxy)
            self._aliases.append(alias)

        logger.info("Discovered %d broker tools", len(self._tools))

        self._build_namespace_docs()
        self.__doc__ = self._build_help_doc()

    def _build_help_doc(self) -> str:
        """Build a comprehensive docstring for help(cel) from discovered tools."""
        lines = ["Celbridge application proxy.", ""]

        top_level_tools: list[dict] = []
        namespaced_tools: dict[str, list[dict]] = {}

        for tool in self._tools:
            alias = tool.get("alias", "")
            if not alias:
                continue
            if "." in alias:
                tool_namespace = alias.split(".", 1)[0]
                if tool_namespace not in namespaced_tools:
                    namespaced_tools[tool_namespace] = []
                namespaced_tools[tool_namespace].append(tool)
            else:
                top_level_tools.append(tool)

        if top_level_tools:
            lines.append("Commands:")
            lines.append("")
            for tool in sorted(top_level_tools, key=lambda t: t.get("alias", "")):
                alias = tool.get("alias", "")
                signature = _build_signature(tool)
                description = tool.get("description", "")
                lines.append(f"  cel.{alias}{signature}")
                if description:
                    lines.append(f"      {description}")
                lines.append("")

        for tool_namespace in sorted(namespaced_tools.keys()):
            tools_in_namespace = namespaced_tools[tool_namespace]
            lines.append(f"  cel.{tool_namespace}:")
            lines.append("")
            for tool in sorted(tools_in_namespace, key=lambda t: t.get("alias", "")):
                alias = tool.get("alias", "")
                method_name = alias.split(".", 1)[1]
                signature = _build_signature(tool)
                description = tool.get("description", "")
                lines.append(f"    .{method_name}{signature}")
                if description:
                    lines.append(f"        {description}")
                lines.append("")

        return "\n".join(lines)

    def _build_namespace_docs(self) -> None:
        """Build __doc__ for each ToolNamespace from its registered methods."""
        namespaced_tools: dict[str, list[dict]] = {}

        for tool in self._tools:
            alias = tool.get("alias", "")
            if "." not in alias:
                continue
            namespace_name = alias.split(".", 1)[0]
            if namespace_name not in namespaced_tools:
                namespaced_tools[namespace_name] = []
            namespaced_tools[namespace_name].append(tool)

        for namespace_name, tools in namespaced_tools.items():
            namespace = getattr(self, namespace_name, None)
            if namespace is None:
                continue

            lines = [f"cel.{namespace_name} commands:", ""]
            for tool in sorted(tools, key=lambda t: t.get("alias", "")):
                alias = tool.get("alias", "")
                method_name = alias.split(".", 1)[1]
                signature = _build_signature(tool)
                description = tool.get("description", "")
                lines.append(f"  cel.{namespace_name}.{method_name}{signature}")
                if description:
                    lines.append(f"      {description}")
                lines.append("")

            namespace.__doc__ = "\n".join(lines)

    def _register_proxy(self, alias: str, proxy) -> None:
        """Register a proxy method on this object, creating namespaces for dotted aliases."""
        if "." in alias:
            parts = alias.split(".", 1)
            namespace_name = parts[0]
            method_name = parts[1]

            namespace = getattr(self, namespace_name, None)
            if namespace is None:
                namespace = ToolNamespace(namespace_name)
                object.__setattr__(self, namespace_name, namespace)

            proxy.__name__ = method_name
            object.__setattr__(namespace, method_name, proxy)
        else:
            proxy.__name__ = alias
            object.__setattr__(self, alias, proxy)

    def __getattr__(self, name: str):
        """Provide a helpful error when an unknown method is accessed."""
        # Find the top-level alias names (the part before any dot)
        top_level_names = sorted(set(
            alias.split(".", 1)[0] for alias in self._aliases
        ))

        matches = difflib.get_close_matches(name, top_level_names, n=3, cutoff=0.5)

        if matches:
            suggestion = ", ".join(f"cel.{m}" for m in matches)
            message = f"cel.{name} is not a known command. Did you mean: {suggestion}?"
        else:
            message = f"cel.{name} is not a known command. Type help(cel) to see available commands."

        raise AttributeError(message)

    def _make_tool_proxy(self, tool_name: str, tool: dict):
        """Create a callable proxy for a single broker tool.

        The proxy accepts positional and keyword arguments. Positional
        arguments are mapped to parameter names in declaration order.
        """
        alias = tool.get("alias", tool_name)
        parameter_names = [p["name"] for p in tool.get("parameters", [])]
        signature = _build_signature(tool)

        def proxy(*args, **kwargs):
            if len(args) > len(parameter_names):
                raise CelError(
                    f"cel.{alias}{signature} was called with "
                    f"{len(args)} positional arguments"
                )

            arguments = {}
            for index, value in enumerate(args):
                camel_name = parameter_names[index]
                arguments[camel_name] = value

            for key, value in kwargs.items():
                arguments[_snake_to_camel(key)] = value

            result = self._client.call("tools/call", name=tool_name, arguments=arguments)

            if result is None:
                return None

            is_success = result.get("isSuccess", False)
            if not is_success:
                error_message = result.get("errorMessage", "Unknown error")
                raise CelError(
                    f"cel.{alias}{signature}: {error_message}"
                )

            return result.get("value")

        proxy.__signature__ = _build_inspect_signature(tool)
        proxy.__module__ = "cel"
        proxy.__qualname__ = alias

        return proxy
