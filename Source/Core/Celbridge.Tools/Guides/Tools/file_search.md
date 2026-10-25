# file_search

Finds files or folders by name or path with a search filter. The rules are in `search_filters`. `*.py` finds every `.py` file in the project, `src/**/*.cs` finds the `.cs` files under `src`, and `*.{ts,tsx}` finds both kinds.

## Pattern semantics

- **A root prefix** picks the root to search: `logs:*.log`. The pattern after the prefix is relative to that root, so `logs:*.log` matches at any depth in `logs:`. A pattern with no prefix, or with `project:`, searches the project.
- **A matched folder takes everything inside it.** With `type: "file"`, `docs/` finds every file under each `docs` folder. With `type: "folder"`, `docs` also finds the folders inside `docs`.
- **An empty pattern** matches every file, or every folder with `type: "folder"`.
