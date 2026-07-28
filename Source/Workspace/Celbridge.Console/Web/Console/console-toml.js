// Minimal TOML parse/serialise for the constrained .console config shape. The console owns this rather
// than the C# host, so ShellSessionProvider takes structured config with no Tomlyn dependency. It handles
// the documented shape (single-line string and string-array values under [session], [session.options],
// [session.environment]); anything outside that raises a config error surfaced in the settings view.

/**
 * @typedef {Object} ConsoleConfig
 * @property {string} type
 * @property {string} executable
 * @property {string[]} arguments
 * @property {string} workingDirectory
 * @property {Object<string,string>} environment
 */

/** Returns a fresh default shell config. */
export function defaultConsoleConfig() {
    return {
        type: 'shell',
        executable: '',
        arguments: [],
        workingDirectory: '',
        environment: {},
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
    const lines = (text || '').split(/\r?\n/);

    for (const rawLine of lines) {
        const line = stripComment(rawLine).trim();
        if (line === '') {
            continue;
        }

        const sectionMatch = line.match(/^\[(.+)\]$/);
        if (sectionMatch) {
            section = sectionMatch[1].trim();
            continue;
        }

        const equalsIndex = line.indexOf('=');
        if (equalsIndex < 0) {
            throw new Error(`Invalid line in .console config: "${line}"`);
        }

        const key = line.slice(0, equalsIndex).trim();
        const rawValue = line.slice(equalsIndex + 1).trim();
        assignValue(config, section, key, rawValue);
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
    if (config.workingDirectory) {
        lines.push(`working_directory = ${quote(config.workingDirectory)}`);
    }

    lines.push('');
    lines.push('[session.options]');
    if (config.executable) {
        lines.push(`executable = ${quote(config.executable)}`);
    }
    if (config.arguments && config.arguments.length > 0) {
        const items = config.arguments.map(quote).join(', ');
        lines.push(`arguments = [${items}]`);
    }

    lines.push('');
    lines.push('[session.environment]');
    for (const [name, value] of Object.entries(config.environment || {})) {
        lines.push(`${name} = ${quote(value)}`);
    }

    return lines.join('\n') + '\n';
}

function assignValue(config, section, key, rawValue) {
    if (section === 'session') {
        if (key === 'type') {
            config.type = parseScalar(rawValue);
        } else if (key === 'working_directory') {
            config.workingDirectory = parseScalar(rawValue);
        }
        // Other [session] keys (e.g. title) are ignored in phase 1.
        return;
    }

    if (section === 'session.options') {
        if (key === 'executable') {
            config.executable = parseScalar(rawValue);
        } else if (key === 'arguments') {
            config.arguments = parseArray(rawValue);
        }
        return;
    }

    if (section === 'session.environment') {
        config.environment[key] = parseScalar(rawValue);
    }
}

// Cuts a line at its first unquoted '#'. A '#' inside a double-quoted value is preserved.
function stripComment(line) {
    let inString = false;
    for (let index = 0; index < line.length; index++) {
        const character = line[index];
        if (character === '"') {
            inString = !inString;
        } else if (character === '#' && !inString) {
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
