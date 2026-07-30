// Pure helpers for the console settings form: parsing the one-per-line textarea formats into config
// structures and back, comparing configs to decide whether a reopen is needed, and shaping the payload the
// host receives. No DOM or session state.

export function splitLines(text) {
    return text.split('\n').map((line) => line.trim()).filter((line) => line !== '');
}

export function parseEnvironmentLines(text) {
    const environment = {};
    for (const rawLine of text.split('\n')) {
        const line = rawLine.trim();
        if (line === '') {
            continue;
        }
        const equalsIndex = line.indexOf('=');
        if (equalsIndex < 0) {
            continue;
        }
        const name = line.slice(0, equalsIndex).trim();
        const value = line.slice(equalsIndex + 1).trim();
        if (name !== '') {
            environment[name] = value;
        }
    }
    return environment;
}

// A runner is edited as one line per runner: comma-separated extensions, then '=', then the command.
export function parseRunnerLines(text) {
    const runners = [];
    for (const rawLine of text.split('\n')) {
        const line = rawLine.trim();
        if (line === '') {
            continue;
        }
        const equalsIndex = line.indexOf('=');
        if (equalsIndex < 0) {
            continue;
        }
        const extensions = line.slice(0, equalsIndex).split(',').map((part) => part.trim()).filter((part) => part !== '');
        const command = line.slice(equalsIndex + 1).trim();
        if (extensions.length === 0 || command === '') {
            continue;
        }
        runners.push({ extensions, command });
    }
    return runners;
}

export function formatRunnerLines(runners) {
    return (runners || [])
        .map((runner) => `${(runner.extensions || []).join(', ')} = ${runner.command || ''}`)
        .join('\n');
}

// A shortcut is edited as one line per shortcut: label, then icon, then injected text, separated by '|'.
// Only the first two pipes delimit, so the injected text may itself be a shell pipeline with its interior
// spacing preserved.
export function parseShortcutLines(text) {
    const shortcuts = [];
    for (const rawLine of text.split('\n')) {
        const line = rawLine.trim();
        if (line === '') {
            continue;
        }
        const firstPipe = line.indexOf('|');
        const secondPipe = firstPipe >= 0 ? line.indexOf('|', firstPipe + 1) : -1;
        let label = '';
        let icon = '';
        let injected = '';
        if (secondPipe >= 0) {
            label = line.slice(0, firstPipe).trim();
            icon = line.slice(firstPipe + 1, secondPipe).trim();
            injected = line.slice(secondPipe + 1).trim();
        } else if (firstPipe >= 0) {
            label = line.slice(0, firstPipe).trim();
            injected = line.slice(firstPipe + 1).trim();
        } else {
            label = line;
        }
        if (label === '' && injected === '') {
            continue;
        }
        shortcuts.push({ label, icon, text: injected });
    }
    return shortcuts;
}

export function formatShortcutLines(shortcuts) {
    return (shortcuts || [])
        .map((shortcut) => `${shortcut.label || ''} | ${shortcut.icon || ''} | ${shortcut.text || ''}`)
        .join('\n');
}

// A comparable view of the config for the "needs a reopen" check: the start payload with a stable env
// order. Shortcuts are excluded because they are a live client-side toolbar, not a launch input. Every
// other field applies on reopen.
export function normalizeConfig(config) {
    const normalized = buildStartConfig(config);
    const environment = {};
    for (const name of Object.keys(normalized.environment).sort()) {
        environment[name] = normalized.environment[name];
    }
    normalized.environment = environment;
    return normalized;
}

export function configsEqual(a, b) {
    return JSON.stringify(normalizeConfig(a)) === JSON.stringify(normalizeConfig(b));
}

// The comparable launch-relevant view of a config, shared by the divergence check.
export function buildStartConfig(config) {
    return {
        type: config.type || 'shell',
        executable: config.executable || '',
        pythonVersion: config.pythonVersion || '',
        arguments: config.arguments || [],
        dependencies: config.dependencies || [],
        workingDirectory: config.workingDirectory || '',
        startupScript: config.startupScript || '',
        environment: config.environment || {},
        runners: (config.runners || []).map((runner) => ({
            extensions: runner.extensions || [],
            command: runner.command || '',
        })),
    };
}
