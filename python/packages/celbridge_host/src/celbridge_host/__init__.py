"""Celbridge host package.

Python host process for Celbridge integration.
Provides REPL environment, JSON-RPC/MCP server, etc.
"""

from celbridge_host.celbridge_host import CelbridgeHost
from celbridge_host.rpc_client import RpcClient
from celbridge_host.rpc_service import RpcError

__all__ = ["CelbridgeHost", "RpcClient", "RpcError"]
