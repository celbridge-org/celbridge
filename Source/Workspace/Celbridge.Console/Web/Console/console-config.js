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

// Comma-separated extensions, as a runner card edits them.
export function parseExtensionList(text) {
    return text.split(',').map((part) => part.trim()).filter((part) => part !== '');
}


// A comparable view of the config for the "needs a reopen" check: the start payload with a stable key
// order. Shortcuts are excluded because they are a live client-side toolbar, not a launch input. Every
// other field applies on reopen.
export function normalizeConfig(config) {
    const normalized = buildStartConfig(config);
    normalized.environment = sortKeys(normalized.environment);
    normalized.sessionTypeOptions = sortKeys(normalized.sessionTypeOptions);
    return normalized;
}

function sortKeys(table) {
    const sorted = {};
    for (const name of Object.keys(table).sort()) {
        sorted[name] = table[name];
    }
    return sorted;
}

export function configsEqual(a, b) {
    return JSON.stringify(normalizeConfig(a)) === JSON.stringify(normalizeConfig(b));
}

// The comparable launch-relevant view of a config. Only the selected type's table is carried.
export function buildStartConfig(config) {
    const type = config.type || 'shell';

    return {
        type,
        workingDirectory: config.workingDirectory || '',
        sessionTypeOptions: (config.optionsBySessionType || {})[type] || {},
        environment: config.environment || {},
        runners: (config.runners || []).map((runner) => ({
            extensions: runner.extensions || [],
            command: runner.command || '',
        })),
        disabledBuiltInRunners: config.disabledBuiltInRunners || [],
        triggers: (config.triggers || []).map((trigger) => ({
            pattern: trigger.pattern || '',
            command: trigger.command || '',
        })),
    };
}
