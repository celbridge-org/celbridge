"""Dynamic proxy that exposes Celbridge application methods via the RPC client."""

from celbridge.rpc_client import RpcClient


def _snake_to_pascal(name: str) -> str:
    """Convert a snake_case name to PascalCase.

    Examples:
        get_app_version -> GetAppVersion
        log -> Log
    """
    return ''.join(word.capitalize() for word in name.split('_'))


class CelProxy:
    """Dynamic proxy for calling Celbridge application methods.

    Method calls on this object are converted to JSON-RPC requests sent to the
    Celbridge application. Python snake_case method names are converted to
    PascalCase to match C# naming conventions.

    Examples:
        >>> cel.get_app_version()
        '1.0.0'
        >>> cel.log(message="Hello from Python")
    """

    def __init__(self, client: RpcClient):
        self._client = client

    def log(self, message: str) -> None:
        """Write a log message to the Celbridge application log.

        Args:
            message: The message to log.
        """
        self._client.notify("Log", message=message)

    def help(self) -> None:
        """Display available commands."""
        print("Use cel.<command>() to call a Celbridge application method.\n")
        print("Available commands:\n")
        print("  cel.get_app_version()")
        print("    Returns the Celbridge application version.\n")
        print("  cel.log(message=\"...\")")
        print("    Writes a message to the Celbridge application log.\n")
        print("  cel.help()")
        print("    Display this help text.\n")

    def __getattr__(self, name: str):
        """Dynamically create a method proxy for any Celbridge application method.

        Converts the Python snake_case method name to PascalCase and sends a
        JSON-RPC request to the Celbridge application. The proxy is cached on
        the instance so subsequent accesses avoid re-creating the closure.
        """
        method_name = _snake_to_pascal(name)

        def method_proxy(**kwargs):
            return self._client.call(method_name, **kwargs)

        method_proxy.__name__ = name
        method_proxy.__doc__ = f"Call Celbridge method '{method_name}'."

        # Cache on the instance so __getattr__ is not called again for this name
        object.__setattr__(self, name, method_proxy)

        return method_proxy
