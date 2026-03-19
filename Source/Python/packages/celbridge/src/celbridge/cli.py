"""CLI command registration for Celbridge.

This module imports commands from command-specific modules and provides
a central registration point for the Typer application.
"""

import inspect
import typer
from celbridge import commands


def register_commands(app: typer.Typer) -> None:
    """Register all commands with the Typer application.
    
    Automatically discovers and registers all command functions from the
    commands package. Command functions should be named with the pattern
    `<command_name>_command` and will be registered as `<command_name>`.
    
    Args:
        app: The Typer application instance to register commands with.
    """
    # Get all members of the commands package
    for name, obj in inspect.getmembers(commands):
        # Skip if not a function or doesn't end with '_command'
        if not inspect.isfunction(obj) or not name.endswith('_command'):
            continue
        
        # Extract command name (remove '_command' suffix)
        command_name = name[:-8]  # Remove '_command'
        
        # Special handling for help command - needs app instance
        if name == 'help_command':
            # Create a wrapper with proper closure
            def make_help_wrapper(help_func):
                def wrapper(command: str = typer.Argument(None, help="Optional command name to get help for")):
                    """Display help information for all commands, or a specific command."""
                    help_func(app, command)
                return wrapper
            
            app.command(command_name)(make_help_wrapper(obj))
        else:
            # Register other commands directly
            app.command(command_name)(obj)
