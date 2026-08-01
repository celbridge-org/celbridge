import { describe, it, expect } from 'vitest';
import {
    splitLines,
    parseEnvironmentLines,
    parseExtensionList,
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

describe('parseExtensionList', () => {
    it('splits on commas and trims each extension', () => {
        expect(parseExtensionList(' .py , .ipy ')).toEqual(['.py', '.ipy']);
    });

    it('drops empty entries so a trailing comma is harmless', () => {
        expect(parseExtensionList('.sh,,')).toEqual(['.sh']);
    });

    it('returns an empty array for blank text', () => {
        expect(parseExtensionList('  ')).toEqual([]);
    });
});

describe('normalizeConfig', () => {
    it('keeps runners, drops only shortcuts, and sorts env keys', () => {
        const config = {
            type: 'python',
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
        expect(normalized.runners).toEqual([{ extensions: ['.py'], command: 'x' }]);
        expect(normalized).not.toHaveProperty('shortcuts');
        expect(Object.keys(normalized.environment)).toEqual(['A', 'B']);
    });
});

describe('configsEqual', () => {
    it('ignores shortcuts and env order', () => {
        const base = {
            type: 'python',
            executable: '',
            arguments: [],
            environment: { A: '1', B: '2' },
            runners: [{ extensions: ['.py'], command: 'x' }],
        };
        const other = {
            type: 'python',
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

    it('is false when the runners differ (they need a reopen now)', () => {
        const runnersX = { type: 'python', runners: [{ extensions: ['.py'], command: 'x' }] };
        const runnersY = { type: 'python', runners: [{ extensions: ['.py'], command: 'y' }] };
        expect(configsEqual(runnersX, runnersY)).toBe(false);
    });
});

describe('buildStartConfig', () => {
    it('carries runners and fills missing fields with defaults', () => {
        const built = buildStartConfig({
            type: 'python',
            runners: [{ extensions: ['.py'], command: '%run "{resource}"' }],
        });
        expect(built).toEqual({
            type: 'python',
            executable: '',
            pythonVersion: '',
            arguments: [],
            dependencies: [],
            workingDirectory: '',
            startupScript: '',
            environment: {},
            runners: [{ extensions: ['.py'], command: '%run "{resource}"' }],
            triggers: [],
        });
    });

    it('carries triggers, so editing one flags the console for a reopen', () => {
        const built = buildStartConfig({
            triggers: [{ pattern: '*.xlsx', command: '%run clean.py' }],
        });
        expect(built.triggers).toEqual([
            { pattern: '*.xlsx', command: '%run clean.py' },
        ]);
    });

    it('carries the startup script through to the payload', () => {
        const built = buildStartConfig({ startupScript: 'import numpy as np\nx = 1' });
        expect(built.startupScript).toBe('import numpy as np\nx = 1');
    });

    it('defaults an empty config to a blank shell payload', () => {
        expect(buildStartConfig({})).toEqual({
            type: 'shell',
            executable: '',
            pythonVersion: '',
            arguments: [],
            dependencies: [],
            workingDirectory: '',
            startupScript: '',
            environment: {},
            runners: [],
            triggers: [],
        });
    });
});
