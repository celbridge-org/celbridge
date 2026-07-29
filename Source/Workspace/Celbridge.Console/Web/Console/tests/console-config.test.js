import { describe, it, expect } from 'vitest';
import {
    splitLines,
    parseEnvironmentLines,
    parseRunnerLines,
    formatRunnerLines,
    parseShortcutLines,
    formatShortcutLines,
    normalizeConfig,
    configsEqual,
    buildStartConfig,
} from '../console-config.js';

describe('splitLines', () => {
    it('trims each line and drops blank ones', () => {
        expect(splitLines('  -q \n\n  --verbose  \n')).toEqual(['-q', '--verbose']);
    });

    it('returns an empty array for blank text', () => {
        expect(splitLines('   \n \n')).toEqual([]);
    });
});

describe('parseEnvironmentLines', () => {
    it('parses KEY=value pairs, trimming names and values', () => {
        expect(parseEnvironmentLines('A=1\n  B = two words \n')).toEqual({ A: '1', B: 'two words' });
    });

    it('keeps only the first = so values may contain =', () => {
        expect(parseEnvironmentLines('URL=http://x/?a=b')).toEqual({ URL: 'http://x/?a=b' });
    });

    it('skips lines without = and lines with an empty name', () => {
        expect(parseEnvironmentLines('novalue\n=orphan\nC=3')).toEqual({ C: '3' });
    });
});

describe('runner lines', () => {
    it('parses comma-separated extensions and the command', () => {
        expect(parseRunnerLines('.py, .ipy = %run "{script_path}"')).toEqual([
            { extensions: ['.py', '.ipy'], command: '%run "{script_path}"' },
        ]);
    });

    it('skips lines with no extensions or no command', () => {
        expect(parseRunnerLines('= nothing\n.py =\n\n.sh = bash')).toEqual([
            { extensions: ['.sh'], command: 'bash' },
        ]);
    });

    it('round-trips through format and parse', () => {
        const runners = [
            { extensions: ['.py', '.ipy'], command: '%run "{script_path}"' },
            { extensions: ['.sh'], command: 'bash "{script_path}"' },
        ];
        expect(parseRunnerLines(formatRunnerLines(runners))).toEqual(runners);
    });
});

describe('shortcut lines', () => {
    it('parses label | icon | text', () => {
        expect(parseShortcutLines('Run tests | bs-play-fill | pytest -q')).toEqual([
            { label: 'Run tests', icon: 'bs-play-fill', text: 'pytest -q' },
        ]);
    });

    it('treats a two-part line as label + text with no icon', () => {
        expect(parseShortcutLines('Clear | clear')).toEqual([
            { label: 'Clear', icon: '', text: 'clear' },
        ]);
    });

    it('keeps pipes and spacing inside the injected text', () => {
        expect(parseShortcutLines('Pipeline | bs-x | ls -la | grep foo')).toEqual([
            { label: 'Pipeline', icon: 'bs-x', text: 'ls -la | grep foo' },
        ]);
    });

    it('round-trips a shortcut whose text is a shell pipeline', () => {
        const shortcuts = [{ label: 'Pipeline', icon: 'bs-x', text: 'ls -la | grep foo' }];
        expect(parseShortcutLines(formatShortcutLines(shortcuts))).toEqual(shortcuts);
    });

    it('skips lines with neither a label nor injected text', () => {
        expect(parseShortcutLines(' | | \nKeep | echo')).toEqual([
            { label: 'Keep', icon: '', text: 'echo' },
        ]);
    });

    it('round-trips a fully-specified shortcut', () => {
        const shortcuts = [{ label: 'Run', icon: 'bs-play', text: 'pytest' }];
        expect(parseShortcutLines(formatShortcutLines(shortcuts))).toEqual(shortcuts);
    });
});

describe('normalizeConfig', () => {
    it('keeps title and runners, drops only shortcuts, and sorts env keys', () => {
        const config = {
            type: 'python',
            title: 'Kept',
            executable: '',
            pythonVersion: '3.13',
            arguments: [],
            dependencies: ['numpy'],
            workingDirectory: '',
            environment: { B: '2', A: '1' },
            runners: [{ extensions: ['.py'], command: 'x' }],
            shortcuts: [{ label: 'L', icon: '', text: 't' }],
        };
        const normalized = normalizeConfig(config);
        expect(normalized.title).toBe('Kept');
        expect(normalized.runners).toEqual([{ extensions: ['.py'], command: 'x' }]);
        expect(normalized).not.toHaveProperty('shortcuts');
        expect(Object.keys(normalized.environment)).toEqual(['A', 'B']);
    });
});

describe('configsEqual', () => {
    it('ignores shortcuts and env order', () => {
        const base = {
            type: 'python',
            title: 'Same',
            executable: '',
            arguments: [],
            environment: { A: '1', B: '2' },
            runners: [{ extensions: ['.py'], command: 'x' }],
        };
        const other = {
            type: 'python',
            title: 'Same',
            executable: '',
            arguments: [],
            environment: { B: '2', A: '1' },
            runners: [{ extensions: ['.py'], command: 'x' }],
            shortcuts: [{ label: 'L', icon: '', text: 't' }],
        };
        expect(configsEqual(base, other)).toBe(true);
    });

    it('is false when a launch-affecting field differs', () => {
        const a = { type: 'shell', executable: 'pwsh' };
        const b = { type: 'shell', executable: 'bash' };
        expect(configsEqual(a, b)).toBe(false);
    });

    it('is false when the title or the runners differ (both need a reopen now)', () => {
        const titleA = { type: 'python', title: 'A' };
        const titleB = { type: 'python', title: 'B' };
        expect(configsEqual(titleA, titleB)).toBe(false);

        const runnersX = { type: 'python', runners: [{ extensions: ['.py'], command: 'x' }] };
        const runnersY = { type: 'python', runners: [{ extensions: ['.py'], command: 'y' }] };
        expect(configsEqual(runnersX, runnersY)).toBe(false);
    });
});

describe('buildStartConfig', () => {
    it('carries runners and title and fills missing fields with defaults', () => {
        const built = buildStartConfig({
            type: 'python',
            title: 'Py',
            runners: [{ extensions: ['.py'], command: '%run "{script_path}"' }],
        });
        expect(built).toEqual({
            type: 'python',
            title: 'Py',
            executable: '',
            pythonVersion: '',
            arguments: [],
            dependencies: [],
            workingDirectory: '',
            environment: {},
            runners: [{ extensions: ['.py'], command: '%run "{script_path}"' }],
        });
    });

    it('defaults an empty config to a blank shell payload', () => {
        expect(buildStartConfig({})).toEqual({
            type: 'shell',
            title: '',
            executable: '',
            pythonVersion: '',
            arguments: [],
            dependencies: [],
            workingDirectory: '',
            environment: {},
            runners: [],
        });
    });
});
