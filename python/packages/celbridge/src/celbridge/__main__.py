import typer
from celbridge.cli import version_command

app = typer.Typer(
    name="celbridge",
    help="Celbridge CLI - Manage Celbridge projects, files, builds, and workflows",
    no_args_is_help=True,
)

# Register commands
app.command("version")(version_command)

# Add a placeholder command to prevent single-command mode
# This forces Typer to display a command list instead of promoting
# the single command to the top level
@app.command("help", hidden=True)
def help_command():
    """Show help (hidden placeholder)."""
    pass


def main():
    """Main entry point."""
    app()


if __name__ == "__main__":
    main()
