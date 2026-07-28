// Minimal TOML parse/serialise for the constrained .console config shape. The console owns this rather
// than the C# host, so the session providers take structured config with no Tomlyn dependency. It handles
// the documented shape (single-line string and string-array values under [session], [session.options],
// [session.environment], plus [[session.runner]] and [[session.shortcut]] array-of-tables); anything
// outside that raises a config error surfaced in the settings view.

/**
 * @typedef {Object} ConsoleRunner
 * @property {string[]} extensions
 * @property {string} command
 */

/**
 * @typedef {Object} ConsoleShortcut
 * @property {string} label
 * @property {string} icon
 * @property {string} text
 */

/**
 * @typedef {Object} ConsoleConfig
 * @property {string} type
 * @property {string} title
 * @property {string} executable
 * @property {string} pythonVersion
 * @property {string[]} arguments
 * @property {string[]} dependencies
 * @property {string} workingDirectory
 * @property {Object<string,string>} environment
 * @property {ConsoleRunner[]} runners
 * @property {ConsoleShortcut[]} shortcuts
 */

/** Returns a fresh default shell config. */
export function defaultConsoleConfig() {
    return {
        type: 'shell',
        title: '',
        executable: '',
        pythonVersion: '',
        arguments: [],
        dependencies: [],
        workingDirectory: '',
        environment: {},
        runners: [],
        shortcuts: [],
    };
}

/**
 * Parses .console TOML into a ConsoleConfig. Throws Error with a human-readable message on malformed input.
 * @param {string} text
 * @returns {ConsoleConfig}
 */
export function parseConsoleToml(text) {
    const config = defaultConsoleConfig();

    let section = '';
    let currentTable = null;
    const lines = (text || '').split(/\r?\n/);

    for (const rawLine of lines) {
        const line = stripComment(rawLine).trim();
        if (line === '') {
            continue;
        }

        // An [[array.of.tables]] header appends a new element to a repeated table.
        const arrayMatch = line.match(/^\[\[(.+)\]\]$/);
        if (arrayMatch) {
            section = arrayMatch[1].trim();
            currentTable = beginArrayTable(config, section);
            continue;
        }

        const sectionMatch = line.match(/^\[(.+)\]$/);
        if (sectionMatch) {
            section = sectionMatch[1].trim();
            currentTable = null;
            continue;
        }

        const equalsIndex = line.indexOf('=');
        if (equalsIndex < 0) {
            throw new Error(`Invalid line in .console config: "${line}"`);
        }

        const key = line.slice(0, equalsIndex).trim();
        const rawValue = line.slice(equalsIndex + 1).trim();
        assignValue(config, section, currentTable, key, rawValue);
    }

    return config;
}

/**
 * Serialises a ConsoleConfig back to .console TOML. Only non-empty optional fields are emitted.
 * @param {ConsoleConfig} config
 * @returns {string}
 */
export function serializeConsoleToml(config) {
    const lines = [];

    lines.push('[session]');
    lines.push(`type = ${quote(config.type || 'shell')}`);
    if (config.title) {
        lines.push(`title = ${quote(config.title)}`);
    }
    if (config.workingDirectory) {
        lines.push(`working_directory = ${quote(config.workingDirectory)}`);
    }

    lines.push('');
    lines.push('[session.options]');
    if (config.executable) {
        lines.push(`executable = ${quote(config.executable)}`);
    }
    if (config.pythonVersion) {
        lines.push(`python_version = ${quote(config.pythonVersion)}`);
    }
    if (config.arguments && config.arguments.length > 0) {
        lines.push(`arguments = [${config.arguments.map(quote).join(', ')}]`);
    }
    if (config.dependencies && config.dependencies.length > 0) {
        lines.push(`dependencies = [${config.dependencies.map(quote).join(', ')}]`);
    }

    lines.push('');
    lines.push('[session.environment]');
    for (const [name, value] of Object.entries(config.environment || {})) {
        lines.push(`${name} = ${quote(value)}`);
    }

    for (const runner of config.runners || []) {
        lines.push('');
        lines.push('[[session.runner]]');
        lines.push(`extensions = [${(runner.extensions || []).map(quote).join(', ')}]`);
        lines.push(`command = ${quote(runner.command || '')}`);
    }

    for (const shortcut of config.shortcuts || []) {
        lines.push('');
        lines.push('[[session.shortcut]]');
        lines.push(`label = ${quote(shortcut.label || '')}`);
        if (shortcut.icon) {
            lines.push(`icon = ${quote(shortcut.icon)}`);
        }
        lines.push(`text = ${quote(shortcut.text || '')}`);
    }

    return lines.join('\n') + '\n';
}

// Appends a fresh element to the array the [[header]] names and returns it, so subsequent keys fill it.
// Unknown array tables get a throwaway object so their keys are parsed and ignored.
function beginArrayTable(config, section) {
    if (section === 'session.runner') {
        const runner = { extensions: [], command: '' };
        config.runners.push(runner);
        return runner;
    }

    if (section === 'session.shortcut') {
        const shortcut = { label: '', icon: '', text: '' };
        config.shortcuts.push(shortcut);
        return shortcut;
    }

    return {};
}

function assignValue(config, section, currentTable, key, rawValue) {
    if (section === 'session') {
        if (key === 'type') {
            config.type = parseScalar(rawValue);
        } else if (key === 'title') {
            config.title = parseScalar(rawValue);
        } else if (key === 'working_directory') {
            config.workingDirectory = parseScalar(rawValue);
        }
        return;
    }

    if (section === 'session.options') {
        if (key === 'executable') {
            config.executable = parseScalar(rawValue);
        } else if (key === 'python_version') {
            config.pythonVersion = parseScalar(rawValue);
        } else if (key === 'arguments') {
            config.arguments = parseArray(rawValue);
        } else if (key === 'dependencies') {
            config.dependencies = parseArray(rawValue);
        }
        return;
    }

    if (section === 'session.environment') {
        config.environment[key] = parseScalar(rawValue);
        return;
    }

    if (section === 'session.runner' && currentTable) {
        if (key === 'extensions') {
            currentTable.extensions = parseArray(rawValue);
        } else if (key === 'command') {
            currentTable.command = parseScalar(rawValue);
        }
        return;
    }

    if (section === 'session.shortcut' && currentTable) {
        if (key === 'label') {
            currentTable.label = parseScalar(rawValue);
        } else if (key === 'icon') {
            currentTable.icon = parseScalar(rawValue);
        } else if (key === 'text') {
            currentTable.text = parseScalar(rawValue);
        }
        return;
    }
}

// Cuts a line at its first unquoted '#'. A '#' inside a quoted value (either quote style) is preserved.
function stripComment(line) {
    let quoteChar = null;
    for (let index = 0; index < line.length; index++) {
        const character = line[index];
        if (quoteChar !== null) {
            if (character === quoteChar) {
                quoteChar = null;
            }
        } else if (character === '"' || character === "'") {
            quoteChar = character;
        } else if (character === '#') {
            return line.slice(0, index);
        }
    }
    return line;
}

function parseScalar(rawValue) {
    if (rawValue.startsWith('"') && rawValue.endsWith('"') && rawValue.length >= 2) {
        const inner = rawValue.slice(1, -1);
        return inner.replace(/\\"/g, '"').replace(/\\\\/g, '\\');
    }
    // TOML literal (single-quoted) strings take their content verbatim, with no escape processing. Used for
    // values that contain double quotes, such as a runner command like: '%run "{script_path}"'.
    if (rawValue.startsWith("'") && rawValue.endsWith("'") && rawValue.length >= 2) {
        return rawValue.slice(1, -1);
    }
    // A bare (unquoted) value is returned verbatim, so numbers and booleans round-trip as text.
    return rawValue;
}

function parseArray(rawValue) {
    if (!rawValue.startsWith('[') || !rawValue.endsWith(']')) {
        throw new Error(`Invalid array value in .console config: "${rawValue}"`);
    }

    const inner = rawValue.slice(1, -1).trim();
    if (inner === '') {
        return [];
    }

    return splitTopLevel(inner).map((item) => parseScalar(item.trim()));
}

// Splits an array body on commas that sit outside a double-quoted string.
function splitTopLevel(inner) {
    const items = [];
    let current = '';
    let inString = false;

    for (let index = 0; index < inner.length; index++) {
        const character = inner[index];
        if (character === '"') {
            inString = !inString;
            current += character;
        } else if (character === ',' && !inString) {
            items.push(current);
            current = '';
        } else {
            current += character;
        }
    }

    if (current.trim() !== '') {
        items.push(current);
    }

    return items;
}

function quote(value) {
    const escaped = String(value).replace(/\\/g, '\\\\').replace(/"/g, '\\"');
    return `"${escaped}"`;
}
