if [[ -n ${CELBRIDGE_CONSOLE_USE_SHELL_PROFILE-} ]]; then
    ZDOTDIR=$_celbridge_user_zdotdir
    if [[ -r "$ZDOTDIR/.zprofile" ]]; then
        source "$ZDOTDIR/.zprofile"
    fi
    _celbridge_user_zdotdir=$ZDOTDIR
    ZDOTDIR=$_celbridge_zdotdir
fi
