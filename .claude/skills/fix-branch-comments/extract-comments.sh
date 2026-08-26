#!/usr/bin/env bash
#
# Emits every comment line the branch added or rewrote, one per line, as:
#
#   path:line<TAB>text
#
# A multi-line comment is emitted once, anchored on its opening line. Reading the
# file at that anchor shows the whole comment.
#
# Usage: extract-comments.sh [base-branch]   (default: main)

set -uo pipefail

BASE=$(git merge-base "${1:-main}" HEAD) || exit 1

KEEP='[.](cs|xaml|py|js|ts|md)$'
DROP='(^|/)(obj|bin|node_modules)/|[.]g[.]cs$|[.]designer[.]cs$|[.]min[.]js$|package-lock[.]json$'

# The comment opener differs by language, and matching the wrong one produces
# noise that reads like a finding: # is a heading in markdown and a preprocessor
# directive in C#, neither of which is a comment.
opener_for() {
  case "$1" in
    *.md | *.xaml) printf '%s' '^[[:space:]]*<!--' ;;
    *.py)          printf '%s' '^[[:space:]]*#' ;;
    *)             printf '%s' '^[[:space:]]*(///|//)' ;;
  esac
}
export -f opener_for

# Tracked files. One range, base to working tree, so a line changed in a commit
# and then again in the working tree is counted once at its current text.
git diff -U0 "$BASE" -- . | awk -v keep="$KEEP" -v drop="$DROP" '
  function opener_for(path) {
    if (path ~ /[.](md|xaml)$/) return "^[ \t]*<!--"
    if (path ~ /[.]py$/)        return "^[ \t]*#"
    return "^[ \t]*(///|//)"
  }
  /^\+\+\+ b\// {
    file = substr($0, 7)
    ok = (file ~ keep) && (file !~ drop)
    opener = opener_for(file)
    next
  }
  /^\+\+\+ / { ok = 0; next }
  /^@@/ {
    match($0, /\+[0-9]+/)
    line = substr($0, RSTART + 1, RLENGTH - 1) + 0
    next
  }
  /^\+/ {
    if (ok) {
      text = substr($0, 2)
      if (text ~ opener) printf "%s:%d\t%s\n", file, line, text
    }
    line++
  }
'

# Untracked files are new in full, so every comment in them is one the branch added.
git ls-files --others --exclude-standard \
  | { grep -E "$KEEP" || true; } \
  | { grep -vE "$DROP" || true; } \
  | while read -r file; do
      grep -nE "$(opener_for "$file")" -- "$file" \
        | sed "s|^\([0-9]*\):|$file:\1\t|"
    done

exit 0
