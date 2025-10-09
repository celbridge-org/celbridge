"""Host wrapper for interacting with the Celbridge CLI."""

import json
import os
import subprocess
import sys
from typing import Any


class CelbridgeCommandError(Exception):
    """
    Exception raised when a Celbridge CLI command fails.
    
    This is used internally to distinguish CLI errors from user code errors,
    allowing us to display CLI errors cleanly without a traceback while
    preserving full tracebacks for user code errors.
    """
    def __init__(self, message: str, command: str | None = None):
        """
        Initialize the exception.
        
        Args:
            message: The error message to display
            command: The command that failed (for help display)
        """
        super().__init__(message)
        self.command = command


def _clean_error_message(stderr: str) -> str:
    """
    Remove usage/help lines from error messages that aren't helpful in REPL context.
    
    Args:
        stderr: The raw stderr output from the CLI command
        
    Returns:
        Cleaned error message with usage lines removed
    """
    lines = stderr.strip().split('\n')
    cleaned_lines = []
    
    for line in lines:
        # Skip usage and help suggestion lines
        if line.startswith('Usage:') or line.startswith('Try '):
            continue
        # Skip uv installation progress messages
        if 'Installed' in line and 'packages in' in line:
            continue
        if line.startswith('Resolved ') or line.startswith('Prepared '):
            continue
        cleaned_lines.append(line)
    
    return '\n'.join(cleaned_lines).strip()


def _run_cli_command_uv(args: list[str]) -> str:
    """
    Run a Celbridge CLI command in an isolated environment using uv.
    
    This executes the celbridge CLI as a subprocess to maintain dependency isolation
    between celbridge_host and celbridge packages.
    
    Args:
        args: Command arguments (without the base command)
        
    Returns:
        Command output as JSON string
        
    Raises:
        CelbridgeCommandError: If command execution fails
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
    
    # Build the command: uv run --cache-dir <dir> --with <celbridge_package_path> python -m celbridge <args>
    full_cmd = [
        uv_path,
        "run",
        "--cache-dir", uv_cache_dir,
        "--with", celbridge_package_path,
        "python", "-m", "celbridge"
    ]
    full_cmd.extend(args)
    
    try:
        result = subprocess.run(
            full_cmd,
            capture_output=True,
            text=True,
            check=True,
        )
        return result.stdout.strip()
    except subprocess.CalledProcessError as e:
        # Clean up error message for REPL users
        error_msg = _clean_error_message(e.stderr)
        # Extract command name from args (first argument is the command)
        command = args[0] if args else None
        raise CelbridgeCommandError(error_msg, command) from None
    except FileNotFoundError as e:
        raise RuntimeError(
            f"Could not find uv executable at: {uv_path}"
        ) from e


def _run_cli_command_python(args: list[str]) -> str:
    """
    Run a Celbridge CLI command using the current Python interpreter.
    
    This is used in development/testing when dependency isolation is not required.
    Assumes celbridge is already installed in the current Python environment.
    
    Args:
        args: Command arguments (without the base command)
        
    Returns:
        Command output as JSON string
        
    Raises:
        CelbridgeCommandError: If command execution fails
    """
    # Build the command: python -m celbridge <args>
    full_cmd = [
        sys.executable,  # Use the current Python interpreter
        "-m", "celbridge"
    ]
    full_cmd.extend(args)
    
    try:
        result = subprocess.run(
            full_cmd,
            capture_output=True,
            text=True,
            check=True,
        )
        return result.stdout.strip()
    except subprocess.CalledProcessError as e:
        # Clean up error message for REPL users
        error_msg = _clean_error_message(e.stderr)
        # Extract command name from args (first argument is the command)
        command = args[0] if args else None
        raise CelbridgeCommandError(error_msg, command) from None
    except FileNotFoundError as e:
        raise RuntimeError(
            f"Could not find Python executable: {sys.executable}"
        ) from e


def _run_cli_command(args: list[str]) -> str:
    """
    Run a Celbridge CLI command using the appropriate execution method.
    
    Uses subprocess isolation when CELBRIDGE_UV_PATH is set (production),
    otherwise falls back to direct import (development/testing).
    
    Args:
        args: Command arguments (without the base command)
        
    Returns:
        Command output as JSON string
        
    Raises:
        RuntimeError: If command execution fails
    """
    # Check if we should use uv for isolation (production mode)
    if os.environ.get("CELBRIDGE_UV_PATH"):
        return _run_cli_command_uv(args)
    else:
        # Fall back to current Python interpreter (development/testing mode)
        return _run_cli_command_python(args)


def _get_command_help(command_name: str) -> dict[str, Any] | None:
    """
    Get help information for a specific command.
    
    Args:
        command_name: The name of the command to get help for
        
    Returns:
        Command help dict or None if command not found or help fails
    """
    try:
        # Get all help data
        output = _run_cli_command(["help"])
        help_data = json.loads(output)
        
        # Find the specific command
        commands = help_data.get("commands", [])
        for cmd in commands:
            if cmd.get("name") == command_name:
                return cmd
        return None
    except Exception:
        # If we can't get help, just return None
        return None


def _format_command_help(cmd_help: dict[str, Any]) -> str:
    """
    Format command help information for display.
    
    Args:
        cmd_help: Command help dictionary from the help command
        
    Returns:
        Formatted help text
    """
    name = cmd_help.get("name", "")
    help_text = cmd_help.get("help", "")
    parameters = cmd_help.get("parameters", [])
    
    lines = [
        f"\nUsage: cel.{name.replace('-', '_')}(",
    ]
    
    # Build parameter list
    param_parts = []
    for param in parameters:
        param_name = param.get("name", "")
        param_type = param.get("type", "str")
        required = param.get("required", False)
        default = param.get("default")
        
        if required:
            param_parts.append(f"{param_name}")
        else:
            default_str = f'"{default}"' if isinstance(default, str) else str(default)
            param_parts.append(f"{param_name}={default_str}")
    
    if param_parts:
        lines[0] += ", ".join(param_parts)
    lines[0] += ")"
    
    # Add description
    if help_text:
        lines.append(help_text)
        
    return "\n".join(lines)


class CelbridgeHost:
    """
    Dynamic proxy for Celbridge CLI commands.
    
    This class automatically proxies method calls to the celbridge CLI.
    Method names are converted to CLI commands (underscores become hyphens).
    All commands return JSON output.
    
    Examples:
        >>> cel = CelbridgeHost()
        >>> cel.version()
        {'version': '0.1.0', 'api': '1.0'}
    """
    
    def help(self) -> None:
        """
        Display help information for all Celbridge commands.
        
        Fetches help data from the celbridge CLI and formats it as
        human-readable text.
        """
        # Get help data from CLI
        output = _run_cli_command(["help"])
        help_data = json.loads(output)
        
        # Format and print help information
        commands = help_data.get("commands", [])
        
        # Print usage instructions at the top
        print(f"Use cel.<command>() to execute a command. Example: cel.version()\n")
        
        # Print commands
        if commands:
            print("Available Commands:\n")
            
            # Find the longest command name for alignment
            max_name_len = max(len(cmd.get("name", "")) for cmd in commands)
            
            for cmd in commands:
                name = cmd.get("name", "")
                help_text = cmd.get("help", "")
                parameters = cmd.get("parameters", [])
                
                # Print command name and help text
                print(f"  {name.ljust(max_name_len)}  {help_text}")
                
                # Print parameters (if any)
                if parameters:
                    for param in parameters:
                        param_name = param.get("name", "")
                        param_type = param.get("type", "str")
                        required = param.get("required", False)
                        default = param.get("default")
                        
                        req_str = "required" if required else "optional"
                        default_str = f", default: {default}" if default else ""
                        
                        print(f"    - {param_name} ({param_type}, {req_str}{default_str})")
            
   
    def __getattr__(self, command: str):
        """
        Dynamically create a wrapper function for any CLI command.
        
        Args:
            command: The command name (method name called on this object)
            
        Returns:
            A callable that executes the CLI command
        """
        def command_wrapper(*args, **kwargs) -> dict[str, Any] | None:
            """
            Execute a CLI command with the given arguments.
            
            Args:
                *args: Positional arguments for the command
                **kwargs: Keyword arguments converted to CLI options (--key value)
                
            Returns:
                Parsed JSON dict with command output
            """
            try:
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
                
                output = _run_cli_command(cmd_args)
                
                # Parse JSON output
                return json.loads(output)
            except CelbridgeCommandError as e:
                # Print the error cleanly without a traceback
                print(e)
                
                # Try to display command-specific help
                if e.command:
                    cmd_help = _get_command_help(e.command)
                    if cmd_help:
                        print(_format_command_help(cmd_help))
                
                return None
        
        # Preserve the command name for better debugging
        command_wrapper.__name__ = f"celbridge_{command}"
        command_wrapper.__doc__ = f"Execute 'celbridge {command.replace('_', '-')}' command."
        
        return command_wrapper
