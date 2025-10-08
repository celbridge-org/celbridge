"""Celbridge host package.

Python host process for Celbridge integration.
Provides REPL environment, JSON-RPC/MCP server, etc.
"""

from celbridge_host.celbridge_host import CelbridgeHost

__all__ = ["CelbridgeHost"]
