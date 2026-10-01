if [[ -n ${CELBRIDGE_CONSOLE_USE_SHELL_PROFILE-} ]]; then
    ZDOTDIR=$_celbridge_user_zdotdir
    if [[ -r "$ZDOTDIR/.zlogin" ]]; then
        source "$ZDOTDIR/.zlogin"
    fi
    _celbridge_user_zdotdir=$ZDOTDIR
    ZDOTDIR=$_celbridge_zdotdir
fi

# Move Celbridge's hooks to the end, so they run after any hooks the user's files added. A hook is added
# back if a user's file replaced the whole list.
precmd_functions=(${precmd_functions:#_celbridge_first_prompt} _celbridge_first_prompt)
if (( ${+functions[_celbridge_compact_prompt]} )); then
    precmd_functions=(${precmd_functions:#_celbridge_compact_prompt} _celbridge_compact_prompt)
fi

# zsh loads the history after its start-up files run, so this history file wins over the user's. Each
# command is written as it is entered, because closing a console kills the shell before it can save.
if [[ -n ${CELBRIDGE_CONSOLE_HISTORY-} ]]; then
    HISTFILE=$CELBRIDGE_CONSOLE_HISTORY
    if (( ! SAVEHIST )); then
        SAVEHIST=1000
    fi
    if (( HISTSIZE < SAVEHIST )); then
        HISTSIZE=$SAVEHIST
    fi
    setopt INC_APPEND_HISTORY
fi
