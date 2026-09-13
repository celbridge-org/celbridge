// The live pty session as this view sees it, over the console/* RPC channel. The session runs host-side from
// the moment the document opens, so this attaches to one already running (console/attach), replays what it
// has painted, and streams from there. The starting veil and the failure overlay are its narration: between
// them the terminal is only uncovered once there is a live screen to show.

import { t } from '/assets/celbridge-client/localization.js';

// The starting veil covers the terminal from launch until the shell reports its screen clear, hiding the
// shell-startup phase.
const VEIL_FADE_MS = 240;

// Backstop for a host that never reports the startup phase ending. The host reveals a quiet session itself
// and notifies, so this only has to outlast its silence window: no output reaches the client while the veil
// is up, which leaves nothing here to measure progress against.
const VEIL_BACKSTOP_MS = 15000;

// Binds this view to the live session.
export function createConsoleSession({ client, term, settings, fitTerminal, waitForTerminalSize }) {
    const sessionStarting = document.getElementById('session-starting');
    const sessionFailed = document.getElementById('session-failed');
    const sessionFailedMessage = document.getElementById('session-failed-message');
    const reopenTerminalButton = document.getElementById('reopen-terminal');

    let veilTimeout = null;
    let veilFadeTimer = null;

    // A resize the session asked for is not one to report back: the session is already at that size.
    let adoptingSessionSize = false;

    let requestInFlight = false;

    // Terminal I/O over the console/* live-session channel. The live session owns the startup-noise
    // trimming, so everything that arrives here is renderable output.
    client.onNotification('console/write', (params) => {
        if (params && typeof params.text === 'string') {
            term.write(params.text);
        }
    });

    client.onNotification('console/sessionState', (params) => {
        if (params && params.state === 'ended') {
            showSessionFailed(t('Console_SessionEnded'));
        }
    });

    // The live session's startup phase is over and its output stream is clean, so reveal the terminal.
    client.onNotification('console/startupComplete', () => {
        hideStartingVeil();
    });

    function sendInput(data) {
        client.sendNotification('console/input', { data });
    }

    term.onData(sendInput);

    term.onResize(({ cols, rows }) => {
        if (adoptingSessionSize) {
            return;
        }

        client.sendNotification('console/resize', { cols, rows });
    });

    // Resizes the terminal to the size the session painted its buffered output at, so nothing is rewrapped.
    function adoptSessionSize(cols, rows) {
        if (!Number.isInteger(cols) ||
            !Number.isInteger(rows) ||
            cols <= 0 ||
            rows <= 0 ||
            (cols === term.cols && rows === term.rows)) {
            return;
        }

        adoptingSessionSize = true;
        try {
            term.resize(cols, rows);
        } finally {
            adoptingSessionSize = false;
        }
    }

    function showSessionFailed(message) {
        hideStartingVeil();
        sessionFailedMessage.textContent = message;
        sessionFailed.classList.remove('hidden');
        settings.setSessionFailure(message);
    }

    function hideSessionFailed() {
        sessionFailed.classList.add('hidden');
        settings.setSessionFailure(null);
    }

    function showStartingVeil() {
        clearVeilTimers();
        sessionStarting.classList.remove('fading-out');
        sessionStarting.classList.remove('hidden');
    }

    function hideStartingVeil() {
        clearVeilTimers();

        if (sessionStarting.classList.contains('hidden')) {
            return;
        }

        sessionStarting.classList.add('fading-out');
        veilFadeTimer = setTimeout(() => {
            veilFadeTimer = null;
            sessionStarting.classList.add('hidden');
            sessionStarting.classList.remove('fading-out');
        }, VEIL_FADE_MS);
    }

    // Arms (or shortens) the safety reveal, so a missed startup-complete notification cannot leave the
    // terminal veiled forever.
    function armVeilTimeout(delayMs) {
        if (sessionStarting.classList.contains('hidden')) {
            return;
        }

        if (veilTimeout !== null) {
            clearTimeout(veilTimeout);
        }

        veilTimeout = setTimeout(() => {
            veilTimeout = null;
            hideStartingVeil();
        }, delayMs);
    }

    function clearVeilTimers() {
        if (veilTimeout !== null) {
            clearTimeout(veilTimeout);
            veilTimeout = null;
        }
        if (veilFadeTimer !== null) {
            clearTimeout(veilFadeTimer);
            veilFadeTimer = null;
        }
    }

    // The terminal size an attach or reopen carries. Zero means there was no box to measure.
    function terminalSize(isSized) {
        if (!isSized) {
            return { cols: 0, rows: 0 };
        }

        return { cols: term.cols, rows: term.rows };
    }

    // Renders an attach or reopen outcome: the launched config drives the pip, the replay fills the
    // terminal, and the state decides between the veil, the failed overlay, and a live prompt.
    function applyAttachResult(result) {
        // The registered session types are static host knowledge that rides along with the attach.
        if (result && Array.isArray(result.sessionTypes)) {
            settings.setSessionTypes(result.sessionTypes);
        }

        settings.setLaunchedConfig(result?.launchedConfigToml ?? null);

        if (!result || result.state === 'failed') {
            showSessionFailed((result && result.error) || t('Console_StartFailed'));
            return;
        }

        if (result.replay) {
            adoptSessionSize(result.cols, result.rows);
            term.write(result.replay);
        }

        if (result.state === 'ended') {
            hideStartingVeil();
            showSessionFailed(t('Console_SessionEnded'));
            return;
        }

        // A session that has not launched yet has nothing on its screen to show, and reports no startup
        // phase until it reaches one.
        if (result.startupPending || result.state === 'starting') {
            showStartingVeil();
            armVeilTimeout(VEIL_BACKSTOP_MS);
        } else {
            hideStartingVeil();
        }

        term.focus();
    }

    // Attaches this view to the live session, which has been running since the document opened. Every step
    // is inside the guard, so a step that throws paints the failure rather than leaving the latch set and
    // the veil up with no way to retry.
    async function attach() {
        if (requestInFlight) {
            return;
        }
        requestInFlight = true;

        try {
            hideSessionFailed();

            // The pty is created at the terminal's measured size, so measure only once the layout has
            // settled.
            await waitForTerminalSize();
            const isSized = fitTerminal();
            term.reset();

            const result = await client.sendRequest('console/attach', terminalSize(isSized));
            applyAttachResult(result);
        } catch (error) {
            showSessionFailed((error && error.message) || String(error));
        } finally {
            requestInFlight = false;
        }
    }

    // Relaunches the session from the file on disk. The form is flushed to the document first, so the file
    // is the single source of truth for what a reopen launches.
    async function reopen() {
        if (requestInFlight) {
            return;
        }
        requestInFlight = true;

        try {
            hideSessionFailed();
            showStartingVeil();

            try {
                await settings.save();
            } catch (error) {
                console.error('[Console] Failed to flush the config before reopen:', error);
            }

            const isSized = fitTerminal();
            term.reset();

            const result = await client.sendRequest('console/reopen', terminalSize(isSized));
            applyAttachResult(result);
        } catch (error) {
            showSessionFailed((error && error.message) || String(error));
        } finally {
            requestInFlight = false;
        }
    }

    reopenTerminalButton.addEventListener('click', () => { reopen(); });

    return {
        attach,
        reopen,
        sendInput,
    };
}
