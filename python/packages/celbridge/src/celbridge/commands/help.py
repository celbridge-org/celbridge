"""Help command for Celbridge CLI.

Provides JSON-formatted help information for all commands.
"""

import inspect
import json
import typer
from typing import Any, Dict, List


def help_command(app: typer.Typer):
    """Display help information for all commands."""
    commands: List[Dict[str, Any]] = []
    
    # Iterate through registered commands in the Typer app
    for command_info in app.registered_commands:
        # Skip hidden commands
        if command_info.hidden:
            continue
        
        # Skip if no callback
        callback = command_info.callback
        if callback is None:
            continue
            
        # Extract command information
        command_name = command_info.name or callback.__name__.replace("_", "-")
        help_text = callback.__doc__ or ""
        # Clean up the help text (remove extra whitespace)
        help_text = " ".join(help_text.split()).strip()
        
        # Get parameters information from the callback signature
        parameters = []
        sig = inspect.signature(callback)
        for param_name, param in sig.parameters.items():
            # Get the type annotation
            param_type = "str"
            if param.annotation != inspect.Parameter.empty:
                if hasattr(param.annotation, "__name__"):
                    param_type = param.annotation.__name__
                else:
                    param_type = str(param.annotation)
            
            # Check if parameter has a default value
            has_default = param.default != inspect.Parameter.empty
            default_value = None if not has_default else str(param.default)
            
            param_info = {
                "name": param_name,
                "type": param_type,
                "required": not has_default,
                "default": default_value,
            }
            parameters.append(param_info)
        
        command_data = {
            "name": command_name,
            "help": help_text,
            "parameters": parameters,
        }
        commands.append(command_data)
    
    output = {
        "commands": commands,
        "app_name": app.info.name or "celbridge",
        "app_help": app.info.help or "",
    }
    
    typer.echo(json.dumps(output, indent=2))
