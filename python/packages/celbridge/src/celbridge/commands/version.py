"""Version command for Celbridge CLI.

Provides version information about the Celbridge package.
"""

import json
import typer
from celbridge import __version__


def version_command():
    """Display version information in JSON format."""
    output = {
        "version": __version__,
        "api": "1.0",
    }
    typer.echo(json.dumps(output))
