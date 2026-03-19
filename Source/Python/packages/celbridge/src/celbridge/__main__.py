import typer
from celbridge.cli import register_commands

app = typer.Typer(
    name="celbridge",
    help="Celbridge CLI - Manage Celbridge projects, files, builds, and workflows",
    no_args_is_help=True,
)

# Register all commands
register_commands(app)


def main():
    """Main entry point."""
    app()


if __name__ == "__main__":
    main()
