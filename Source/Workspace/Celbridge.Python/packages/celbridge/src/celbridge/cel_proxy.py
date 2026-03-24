"""Dynamic proxy that exposes Celbridge broker tools via the RPC client.

On construction, queries the broker's tools/list endpoint and generates a
proxy method for each discovered tool. Tools are registered using their alias
which provides short, natural method names. Dotted aliases (e.g. "app.version")
create namespace objects so that `cel.app.version()` works naturally.
"""

import difflib
import json
import logging

from celbridge.rpc_client import RpcClient
from celbridge.tool_types import (
    snake_to_camel,
    build_signature,
    build_docstring,
    build_inspect_signature,
    partition_tools_by_namespace,
    format_namespace_doc,
)

logger = logging.getLogger(__name__)


class CelError(Exception):
    """Base exception for cel proxy errors.

    IPython is configured to display these without a traceback,
    showing only the error type and message for a cleaner REPL experience.
    """
    pass


class ToolNamespace:
    """A namespace object for grouping related tool methods under a dotted path.

    For example, cel.app is a ToolNamespace that contains cel.app.version().
    """

    def __init__(self, name: str):
        self._namespace_name = name

    def __repr__(self) -> str:
        return f"<ToolNamespace '{self._namespace_name}'>"


class CelProxy:
    """Celbridge application proxy. Type help(cel) for commands."""

    def __init__(self, client: RpcClient):
        self._client = client
        self._tools: list[dict] = []
        self._aliases: list[str] = []
        self._discover_tools()

    def __repr__(self) -> str:
        tool_count = len(self._tools)
        return f"cel - Celbridge proxy ({tool_count} tools). Type help(cel) for commands."

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
            proxy.__doc__ = build_docstring(tool)

            self._register_proxy(alias, proxy)
            self._aliases.append(alias)

        logger.info("Discovered %d broker tools", len(self._tools))

        self._build_namespace_docs()
        self.__doc__ = self._build_help_doc()

    def _build_help_doc(self) -> str:
        """Build a comprehensive docstring for help(cel) from discovered tools."""
        lines = [
            "Provides Python access to the Celbridge application via RPC.",
            "Use help(cel.<namespace>) for details on a specific group.",
            "Use help(<command>) for detailed help on a specific command.",
            "",
        ]

        top_level_tools, namespaced_tools = partition_tools_by_namespace(self._tools)

        if top_level_tools:
            for tool in sorted(top_level_tools, key=lambda t: t.get("alias", "")):
                alias = tool.get("alias", "")
                signature = build_signature(tool)
                description = tool.get("description", "")
                lines.append(f"cel.{alias}{signature}")
                if description:
                    lines.append(f"    {description}")
                lines.append("")

        for namespace_name in sorted(namespaced_tools.keys()):
            lines.append(format_namespace_doc(namespace_name, namespaced_tools[namespace_name]))

        lines.append("cel.tools()")
        lines.append("    Print tool descriptors as JSON")

        return "\n".join(lines)

    def _build_namespace_docs(self) -> None:
        """Build __doc__ for each ToolNamespace from its registered methods."""
        _, namespaced_tools = partition_tools_by_namespace(self._tools)

        for namespace_name, tools in namespaced_tools.items():
            namespace = getattr(self, namespace_name, None)
            if namespace is None:
                continue
            namespace.__doc__ = format_namespace_doc(namespace_name, tools)

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

    def _get_namespace_names(self) -> list[str]:
        """Return the names of all ToolNamespace objects attached to this proxy."""
        return [
            key for key in self.__dict__
            if isinstance(self.__dict__[key], ToolNamespace)
        ]

    def tools(self):
        """Prints the tool descriptors from the broker as formatted JSON."""
        print(json.dumps(self._tools, indent=2))

    def __getattr__(self, name: str):
        """Provide a helpful error when an unknown method is accessed."""
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

        The returned function accepts positional and keyword arguments.
        Positional arguments are mapped to parameter names in declaration
        order; keyword arguments are converted from snake_case to camelCase
        before being sent as JSON-RPC arguments.
        """
        alias = tool.get("alias", tool_name)
        parameter_names = [p["name"] for p in tool.get("parameters", [])]
        signature = build_signature(tool)

        def proxy(*args, **kwargs):
            """Forward a tool call to the Celbridge application via JSON-RPC."""
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
                arguments[snake_to_camel(key)] = value

            result = self._client.call("tools/call", name=tool_name, arguments=arguments)

            if result is None:
                return None

            is_success = result.get("isSuccess", False)
            if not is_success:
                error_message = result.get("errorMessage", "Unknown error")
                raise CelError(
                    f"cel.{alias}{signature}\n{error_message}"
                )

            return result.get("value")

        proxy.__signature__ = build_inspect_signature(tool)
        proxy.__module__ = "cel"
        proxy.__qualname__ = alias

        return proxy


# Override the class name shown by help() so it displays
# "Help on cel object" instead of "Help on CelProxy in module celbridge.cel_proxy"
CelProxy.__name__ = "cel"
CelProxy.__qualname__ = "cel"
CelProxy.__module__ = "celbridge"
