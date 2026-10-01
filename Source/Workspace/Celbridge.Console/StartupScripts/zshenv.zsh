# zsh reads this file first. Save Celbridge's values before any of the user's files can change them.
typeset -gA _celbridge_snapshot
for _celbridge_name in ${=CELBRIDGE_CONSOLE_RESTORE}; do
    _celbridge_snapshot[$_celbridge_name]=${(P)_celbridge_name}
done
unset _celbridge_name
_celbridge_zdotdir=$ZDOTDIR
_celbridge_user_zdotdir=${CELBRIDGE_CONSOLE_USER_ZDOTDIR:-$HOME}

if [[ -n ${CELBRIDGE_CONSOLE_USE_SHELL_PROFILE-} ]]; then
    ZDOTDIR=$_celbridge_user_zdotdir
    if [[ -r "$ZDOTDIR/.zshenv" ]]; then
        source "$ZDOTDIR/.zshenv"
    fi
    _celbridge_user_zdotdir=$ZDOTDIR
    ZDOTDIR=$_celbridge_zdotdir
fi

# Runs once, just before the first prompt. Removes the uv and Python settings the user's files added,
# and restores Celbridge's. Then shows the console and runs its command.
_celbridge_first_prompt() {
    emulate -L zsh
    precmd_functions=(${precmd_functions:#_celbridge_first_prompt})

    # Go back to the working folder, since a user's file may have changed it. Do this first, so the
    # restore below undoes anything a folder change set up. -q skips the user's folder-change hooks.
    local -a _celbridge_command
    if [[ -n $CELBRIDGE_CONSOLE_COMMAND ]]; then
        _celbridge_command=("${(@f)CELBRIDGE_CONSOLE_COMMAND}")
    fi
    if [[ -n $CELBRIDGE_CONSOLE_WORKING_FOLDER ]]; then
        builtin cd -q -- "$CELBRIDGE_CONSOLE_WORKING_FOLDER"
    fi

    local _celbridge_name
    for _celbridge_name in ${(k)parameters}; do
        case $_celbridge_name in
            ({{KEPT_PATTERNS}}) ;;
            ({{REMOVED_PATTERNS}}) unset $_celbridge_name ;;
        esac
    done

    for _celbridge_name in ${(k)_celbridge_snapshot}; do
        export "$_celbridge_name=${_celbridge_snapshot[$_celbridge_name]}"
    done

    local -a _celbridge_folders
    _celbridge_folders=(${(s.:.)CELBRIDGE_CONSOLE_PATH_FOLDERS})
    path=($_celbridge_folders ${path:|_celbridge_folders})

    for _celbridge_name in {{COMMAND_NAMES}}; do
        unalias $_celbridge_name 2>/dev/null
        unfunction $_celbridge_name 2>/dev/null
    done
    rehash

    # Point ZDOTDIR back at the user's folder, so a shell started from the console reads their files.
    if [[ -n $CELBRIDGE_CONSOLE_USER_ZDOTDIR || $_celbridge_user_zdotdir != $HOME ]]; then
        export ZDOTDIR=$_celbridge_user_zdotdir
    else
        unset ZDOTDIR
    fi

    unset -m 'CELBRIDGE_CONSOLE_*'
    unset _celbridge_snapshot _celbridge_zdotdir _celbridge_user_zdotdir

    # The console becomes visible at the marker. The command runs last, because nothing after it
    # would run until it exits.
    clear
    printf '\033]7000;CELBRIDGE-CONSOLE-READY\007'
    if (( ${#_celbridge_command} )); then
        "${_celbridge_command[@]}"
    fi
}

precmd_functions+=(_celbridge_first_prompt)
if [[ -n ${CELBRIDGE_CONSOLE_COMPACT_PROMPT-} ]]; then
    _celbridge_compact_prompt() {
        emulate -L zsh
        PROMPT='%F{cyan}%1~%f %# '
        RPROMPT=
    }
    precmd_functions+=(_celbridge_compact_prompt)
fi
