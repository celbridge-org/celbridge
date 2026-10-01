# Save Celbridge's values before any start-up file can change them.
for _celbridge_name in $CELBRIDGE_CONSOLE_RESTORE; do
    printf -v "_celbridge_saved_$_celbridge_name" '%s' "${!_celbridge_name}"
done
unset _celbridge_name

if [ -r /etc/profile ]; then
    . /etc/profile
fi

# When the shell profile is on, run the first of the user's login files that exists, as a login shell does.
if [ -n "${CELBRIDGE_CONSOLE_USE_SHELL_PROFILE-}" ]; then
    for _celbridge_file in "$HOME/.bash_profile" "$HOME/.bash_login" "$HOME/.profile"; do
        if [ -r "$_celbridge_file" ]; then
            . "$_celbridge_file"
            break
        fi
    done
    unset _celbridge_file
fi

# Runs once, just before the first prompt. Removes the uv and Python settings the start-up files added,
# and restores Celbridge's. Then shows the console and runs its command. The last exit status is kept,
# because a prompt may show it. The hook turns off set -u and resets IFS while it runs, so the options
# the user's files set do not change how it runs.
_celbridge_first_prompt() {
    local _celbridge_status=$?
    local _celbridge_options=$-
    set +u
    local IFS=$' \t\n'
    local _celbridge_name _celbridge_saved _celbridge_folder _celbridge_index

    # Go back to the working folder, since a user's file may have changed it. Do this first, so the
    # restore below undoes anything a folder change set up. builtin skips any cd function the user
    # defined.
    local -a _celbridge_command=()
    local _celbridge_argument
    if [ -n "$CELBRIDGE_CONSOLE_COMMAND" ]; then
        while IFS= read -r _celbridge_argument; do
            _celbridge_command+=("$_celbridge_argument")
        done <<< "$CELBRIDGE_CONSOLE_COMMAND"
    fi
    if [ -n "$CELBRIDGE_CONSOLE_WORKING_FOLDER" ]; then
        builtin cd -- "$CELBRIDGE_CONSOLE_WORKING_FOLDER"
    fi

    for _celbridge_name in $(compgen -v); do
        case $_celbridge_name in
            {{KEPT_PATTERNS}}) ;;
            {{REMOVED_PATTERNS}}) unset "$_celbridge_name" ;;
        esac
    done

    for _celbridge_name in $CELBRIDGE_CONSOLE_RESTORE; do
        _celbridge_saved="_celbridge_saved_$_celbridge_name"
        export "$_celbridge_name=${!_celbridge_saved}"
        unset "$_celbridge_saved"
    done

    local -a _celbridge_folders
    IFS=: read -r -a _celbridge_folders <<< "$CELBRIDGE_CONSOLE_PATH_FOLDERS"
    for (( _celbridge_index = ${#_celbridge_folders[@]} - 1; _celbridge_index >= 0; _celbridge_index-- )); do
        _celbridge_folder=${_celbridge_folders[_celbridge_index]}
        if [ -z "$_celbridge_folder" ]; then
            continue
        fi
        PATH=":$PATH:"
        while [[ $PATH == *":$_celbridge_folder:"* ]]; do
            PATH=${PATH//":$_celbridge_folder:"/:}
        done
        PATH=${PATH#:}
        PATH=${PATH%:}
        PATH="$_celbridge_folder${PATH:+:$PATH}"
    done

    for _celbridge_name in {{COMMAND_NAMES}}; do
        unalias "$_celbridge_name" 2>/dev/null
        unset -f "$_celbridge_name" 2>/dev/null
    done
    hash -r

    # Replace this hook with the one that runs before every later prompt. That hook writes each command
    # to the history as it is entered, because closing a console kills the shell before it can save.
    # With the compact prompt on, it also sets the prompt, starting with this one.
    local _celbridge_lasting=$_celbridge_history_save
    if [ -n "$CELBRIDGE_CONSOLE_COMPACT_PROMPT" ]; then
        _celbridge_lasting="$_celbridge_lasting${_celbridge_newline}_celbridge_compact_prompt"
        _celbridge_compact_prompt
    fi

    # Another prompt command can rearrange PROMPT_COMMAND during this prompt, so this hook is found by
    # name and replaced where it now is.
    local -a _celbridge_entries=()
    local _celbridge_entry _celbridge_is_array= _celbridge_replaced=
    if [[ "$(declare -p PROMPT_COMMAND 2>/dev/null)" == "declare -a"* ]]; then
        _celbridge_is_array=1
        _celbridge_entries=("${PROMPT_COMMAND[@]}")
    else
        while IFS= read -r _celbridge_entry; do
            _celbridge_entries+=("$_celbridge_entry")
        done <<< "$PROMPT_COMMAND"
    fi
    for _celbridge_index in "${!_celbridge_entries[@]}"; do
        if [ "${_celbridge_entries[_celbridge_index]}" = _celbridge_first_prompt ]; then
            _celbridge_entries[_celbridge_index]=$_celbridge_lasting
            _celbridge_replaced=1
        fi
    done
    if [ -z "$_celbridge_replaced" ]; then
        _celbridge_entries+=("$_celbridge_lasting")
    fi
    if [ -n "$_celbridge_is_array" ]; then
        PROMPT_COMMAND=("${_celbridge_entries[@]}")
    else
        PROMPT_COMMAND=
        for _celbridge_entry in "${_celbridge_entries[@]}"; do
            PROMPT_COMMAND="${PROMPT_COMMAND:+$PROMPT_COMMAND$_celbridge_newline}$_celbridge_entry"
        done
    fi
    unset _celbridge_newline _celbridge_history_save

    for _celbridge_name in ${!CELBRIDGE_CONSOLE_@}; do
        unset "$_celbridge_name"
    done

    # The console becomes visible at the marker. The command runs last, because nothing after it
    # would run until it exits.
    clear
    printf '\033]7000;CELBRIDGE-CONSOLE-READY\007'
    if [ ${#_celbridge_command[@]} -gt 0 ]; then
        "${_celbridge_command[@]}"
    fi

    case $_celbridge_options in
        *u*) set -u ;;
    esac
    return $_celbridge_status
}

# Set before every prompt, because a prompt framework may rebuild its prompt before each one.
if [ -n "${CELBRIDGE_CONSOLE_COMPACT_PROMPT-}" ]; then
    _celbridge_compact_prompt() {
        PS1='\[\e[36m\]\W\[\e[m\] \$ '
    }
fi

# Add the hook last, so it runs after any prompt commands the files added. A newline separates it,
# because an existing prompt command may end in a semicolon.
_celbridge_newline=$'\n'
if [[ "$(declare -p PROMPT_COMMAND 2>/dev/null)" == "declare -a"* ]]; then
    PROMPT_COMMAND+=(_celbridge_first_prompt)
else
    PROMPT_COMMAND="${PROMPT_COMMAND:+$PROMPT_COMMAND$_celbridge_newline}_celbridge_first_prompt"
fi

# bash loads the history after this file runs, so this history file wins over the others. Commands
# are appended, so other consoles' commands are kept. bash 3.2, which macOS ships, cannot append to a
# file that was empty at start-up, so a first session there writes the whole file instead.
_celbridge_history_save=:
if [ -n "${CELBRIDGE_CONSOLE_HISTORY-}" ]; then
    HISTFILE=$CELBRIDGE_CONSOLE_HISTORY
    shopt -s histappend
    _celbridge_history_save='history -a'
    if [ "${BASH_VERSINFO[0]}" -lt 4 ] && [ ! -s "$HISTFILE" ]; then
        _celbridge_history_save='history -w'
    fi
fi
