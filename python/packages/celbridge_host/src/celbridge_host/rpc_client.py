"""RPC client for calling C# methods from Python via JSON-RPC."""

import logging

from celbridge_host.rpc_service import call_csharp_method, RpcError

logger = logging.getLogger(__name__)


class RpcClient:
    """Client for making RPC calls from Python to C# via JSON-RPC."""

    def __init__(self):
        """Initialize the RPC client."""
        pass

    def log_message(self, message: str) -> bool:
        """Writes a log message via the Celbridge application."""
        try:
            call_csharp_method("LogMessageAsync", message=message)
            return True
        except RpcError as e:
            logger.error(f"Failed to log message: {e}")
            return False
