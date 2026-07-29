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
export function parseShortcutLines(text) {
    const shortcuts = [];
    for (const rawLine of text.split('\n')) {
        const line = rawLine.trim();
        if (line === '') {
            continue;
        }
        const parts = line.split('|').map((part) => part.trim());
        const label = parts[0] || '';
        let icon = '';
        let injected = '';
        if (parts.length >= 3) {
            icon = parts[1];
            injected = parts.slice(2).join('|').trim();
        } else if (parts.length === 2) {
            injected = parts[1];
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

// A comparable view of the config for the "needs a reopen" check, with a stable env order. Shortcuts are
// excluded because they are a live client-side toolbar, not a launch input. Every other field applies on
// reopen.
export function normalizeConfig(config) {
    const environment = {};
    for (const name of Object.keys(config.environment || {}).sort()) {
        environment[name] = config.environment[name];
    }
    return {
        type: config.type || 'shell',
        title: config.title || '',
        executable: config.executable || '',
        pythonVersion: config.pythonVersion || '',
        arguments: config.arguments || [],
        dependencies: config.dependencies || [],
        workingDirectory: config.workingDirectory || '',
        environment,
        runners: (config.runners || []).map((runner) => ({
            extensions: runner.extensions || [],
            command: runner.command || '',
        })),
    };
}

export function configsEqual(a, b) {
    return JSON.stringify(normalizeConfig(a)) === JSON.stringify(normalizeConfig(b));
}

// The full config payload the host receives on start and on a live update.
export function buildStartConfig(config) {
    return {
        type: config.type || 'shell',
        title: config.title || '',
        executable: config.executable || '',
        pythonVersion: config.pythonVersion || '',
        arguments: config.arguments || [],
        dependencies: config.dependencies || [],
        workingDirectory: config.workingDirectory || '',
        environment: config.environment || {},
        runners: (config.runners || []).map((runner) => ({
            extensions: runner.extensions || [],
            command: runner.command || '',
        })),
    };
}
