"""RPC handler for receiving calls from C# TO Python."""

import sys
import logging
import platform

from jsonrpcserver import method, Result, Success, Error

logger = logging.getLogger(__name__)

# RPC methods exposed to C#

@method
def get_version() -> Result:
    """Return the celbridge package version."""
    try:
        from celbridge_host.celbridge_host import CelbridgeHost
        cel = CelbridgeHost()
        result = cel.version()
        # Extract version string from result dict
        version_str = result.get('version', 'unknown') if isinstance(result, dict) else str(result)
        return Success(version_str)
    except Exception as e:
        logger.error(f"Error in get_version(): {e}")
        return Error(code=-32603, message=f"Internal error: {str(e)}")


@method
def get_system_info() -> Result:
    """Get Python system information."""
    try:
        info = {
            "OS": platform.system(),
            "PythonVersion": sys.version.split()[0],
            "Platform": platform.platform()
        }
        return Success(info)
    except Exception as e:
        logger.error(f"Error in get_system_info(): {e}")
        return Error(code=-32603, message=f"Internal error: {str(e)}")
