if [[ -n ${CELBRIDGE_CONSOLE_USE_SHELL_PROFILE-} ]]; then
    ZDOTDIR=$_celbridge_user_zdotdir
    if [[ -r "$ZDOTDIR/.zshrc" ]]; then
        source "$ZDOTDIR/.zshrc"
    fi
    _celbridge_user_zdotdir=$ZDOTDIR
    ZDOTDIR=$_celbridge_zdotdir
fi
