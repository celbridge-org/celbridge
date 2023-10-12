# Using Github

Some notes on how we use the various features of github and git.

# Commit Messages

We use a simplified Angular-style format for commit messages.

```
Header: <type>:<brief description>
<Blank line>
Body: <detailed description>
<Blank line>
Footer (optional): Links to issue and related information
```

The type field should be one of the following:
```
build: Changes that affect the build system or external dependencies
ci: Changes to our CI configuration files and scripts
docs: Documentation only changes
feat: A new feature
fix: A bug fix
perf: A code change that improves performance
refactor: A code change that neither fixes a bug nor adds a feature
test: Adding missing tests or correcting existing tests
```

More information about writing good commit messages:

* https://github.com/angular/angular/blob/main/CONTRIBUTING.md#commit
* https://thoughtbot.com/blog/5-useful-tips-for-a-better-commit-message
* https://tbaggery.com/2008/04/19/a-note-about-git-commit-messages.html
