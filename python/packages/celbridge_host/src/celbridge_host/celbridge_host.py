"""Host wrapper for interacting with the Celbridge CLI."""

import json
import subprocess
from typing import Any, Dict, List, Union


def _run_cli_command(args: List[str], format: str = "json") -> str:
    """
    Run a Celbridge CLI command and return the output.
    
    Args:
        args: Command arguments (without the base command)
        format: Output format (json or text)
        
    Returns:
        Command output as string
        
    Raises:
        subprocess.CalledProcessError: If command fails
        RuntimeError: If command execution fails
    """
    full_cmd = ["celbridge"] + args + ["--format", format]
    
    try:
        result = subprocess.run(
            full_cmd,
            capture_output=True,
            text=True,
            check=True,
        )
        return result.stdout.strip()
    except subprocess.CalledProcessError as e:
        raise RuntimeError(f"CLI command failed: {e.stderr}") from e
    except FileNotFoundError as e:
        raise RuntimeError(
            "Could not find celbridge command. "
            "Please ensure celbridge is installed."
        ) from e


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

