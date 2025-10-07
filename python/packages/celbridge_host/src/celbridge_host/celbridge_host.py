"""Host wrapper for interacting with the Celbridge CLI."""

import json
import os
import subprocess
import sys
from typing import Any, Dict, List, Union


def _run_cli_command_uv(args: List[str], format: str = "json") -> str:
    """
    Run a Celbridge CLI command in an isolated environment using uv.
    
    This executes the celbridge CLI as a subprocess to maintain dependency isolation
    between celbridge_host and celbridge packages.
    
    Args:
        args: Command arguments (without the base command)
        format: Output format (json or text)
        
    Returns:
        Command output as string
        
    Raises:
        RuntimeError: If command execution fails
    """
    # Get uv path from environment variable set by the host application
    uv_path = os.environ.get("CELBRIDGE_UV_PATH")
    if not uv_path:
        raise RuntimeError(
            "CELBRIDGE_UV_PATH environment variable not set. "
            "The host application must set this to the path of the uv executable."
        )
    
    # Get celbridge package path (wheel or directory)
    celbridge_package_path = os.environ.get("CELBRIDGE_PACKAGE_PATH")
    if not celbridge_package_path:
        raise RuntimeError(
            "CELBRIDGE_PACKAGE_PATH environment variable not set. "
            "The host application must set this to the path of the celbridge package."
        )
    
    # Get uv cache dir (required to ensure isolated installation)
    uv_cache_dir = os.environ.get("CELBRIDGE_UV_CACHE_DIR")
    if not uv_cache_dir:
        raise RuntimeError(
            "CELBRIDGE_UV_CACHE_DIR environment variable not set. "
            "The host application must set this to the uv cache directory."
        )
    
    # Build the command: uv run --cache-dir <dir> --with <celbridge_package_path> python -m celbridge <args> --format <format>
    full_cmd = [
        uv_path,
        "run",
        "--cache-dir", uv_cache_dir,
        "--with", celbridge_package_path,
        "python", "-m", "celbridge"
    ]
    full_cmd.extend(args)
    full_cmd.extend(["--format", format])
    
    try:
        result = subprocess.run(
            full_cmd,
            capture_output=True,
            text=True,
            check=True,
        )
        return result.stdout.strip()
    except subprocess.CalledProcessError as e:
        raise RuntimeError(
            f"CLI command failed (exit code {e.returncode}): {e.stderr.strip()}"
        ) from e
    except FileNotFoundError as e:
        raise RuntimeError(
            f"Could not find uv executable at: {uv_path}"
        ) from e


def _run_cli_command_python(args: List[str], format: str = "json") -> str:
    """
    Run a Celbridge CLI command using the current Python interpreter.
    
    This is used in development/testing when dependency isolation is not required.
    Assumes celbridge is already installed in the current Python environment.
    
    Args:
        args: Command arguments (without the base command)
        format: Output format (json or text)
        
    Returns:
        Command output as string
        
    Raises:
        RuntimeError: If command execution fails
    """
    # Build the command: python -m celbridge <args> --format <format>
    full_cmd = [
        sys.executable,  # Use the current Python interpreter
        "-m", "celbridge"
    ]
    full_cmd.extend(args)
    full_cmd.extend(["--format", format])
    
    try:
        result = subprocess.run(
            full_cmd,
            capture_output=True,
            text=True,
            check=True,
        )
        return result.stdout.strip()
    except subprocess.CalledProcessError as e:
        raise RuntimeError(
            f"CLI command failed (exit code {e.returncode}): {e.stderr.strip()}"
        ) from e
    except FileNotFoundError as e:
        raise RuntimeError(
            f"Could not find Python executable: {sys.executable}"
        ) from e


def _run_cli_command(args: List[str], format: str = "json") -> str:
    """
    Run a Celbridge CLI command using the appropriate execution method.
    
    Uses subprocess isolation when CELBRIDGE_UV_PATH is set (production),
    otherwise falls back to direct import (development/testing).
    
    Args:
        args: Command arguments (without the base command)
        format: Output format (json or text)
        
    Returns:
        Command output as string
        
    Raises:
        RuntimeError: If command execution fails
    """
    # Check if we should use uv for isolation (production mode)
    if os.environ.get("CELBRIDGE_UV_PATH"):
        return _run_cli_command_uv(args, format)
    else:
        # Fall back to current Python interpreter (development/testing mode)
        return _run_cli_command_python(args, format)


class CelbridgeHost:
    """
    Dynamic proxy for Celbridge CLI commands.
    
    This class automatically proxies method calls to the celbridge CLI.
    Method names are converted to CLI commands (underscores become hyphens).
    
    Examples:
        >>> cel = CelbridgeHost()
        >>> cel.version(format="json")
        {'name': 'celbridge', 'version': '0.1.0', 'api': '1.0'}
        
        >>> cel.version(format="text")
        'celbridge 0.1.0'        
    """
    
    def __getattr__(self, command: str):
        """
        Dynamically create a wrapper function for any CLI command.
        
        Args:
            command: The command name (method name called on this object)
            
        Returns:
            A callable that executes the CLI command
        """
        def command_wrapper(*args, format: str = "json", **kwargs) -> Union[Dict[str, Any], str]:
            """
            Execute a CLI command with the given arguments.
            
            Args:
                *args: Positional arguments for the command
                format: Output format ('json' or 'text')
                **kwargs: Keyword arguments converted to CLI options (--key value)
                
            Returns:
                Parsed JSON dict if format='json', otherwise raw string output
            """
            # Convert command name from Python style to CLI style
            cli_command = command.replace('_', '-')
            cmd_args = [cli_command]
            
            # Add positional arguments
            cmd_args.extend(str(arg) for arg in args)
            
            # Add keyword arguments as CLI options
            for key, value in kwargs.items():
                option_name = key.replace('_', '-')
                if isinstance(value, bool):
                    # Boolean flags
                    if value:
                        cmd_args.append(f"--{option_name}")
                else:
                    # Key-value options
                    cmd_args.extend([f"--{option_name}", str(value)])
            
            output = _run_cli_command(cmd_args, format=format)
            
            # Parse JSON output if requested
            if format == "json":
                return json.loads(output)
            return output
        
        # Preserve the command name for better debugging
        command_wrapper.__name__ = f"celbridge_{command}"
        command_wrapper.__doc__ = f"Execute 'celbridge {command.replace('_', '-')}' command."
        
        return command_wrapper


# Global instance for convenience
cel = CelbridgeHost()

