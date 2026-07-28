import { describe, it, expect } from 'vitest';
import {
    defaultConsoleConfig,
    parseConsoleToml,
    serializeConsoleToml,
} from '../console-toml.js';

describe('defaultConsoleConfig', () => {
    it('returns a blank shell config', () => {
        expect(defaultConsoleConfig()).toEqual({
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
        });
    });
});

describe('parseConsoleToml', () => {
    it('parses a full config across all sections', () => {
        const toml = [
            '[session]',
            'type = "shell"',
            'title = "Build"',
            'working_directory = "tools"',
            '',
            '[session.options]',
            'executable = "pwsh"',
            'arguments = ["-NoLogo", "-NoProfile"]',
            '',
            '[session.environment]',
            'BUILD_CONFIG = "Debug"',
        ].join('\n');

        expect(parseConsoleToml(toml)).toEqual({
            type: 'shell',
            title: 'Build',
            executable: 'pwsh',
            pythonVersion: '',
            arguments: ['-NoLogo', '-NoProfile'],
            dependencies: [],
            workingDirectory: 'tools',
            environment: { BUILD_CONFIG: 'Debug' },
            runners: [],
            shortcuts: [],
        });
    });

    it('parses dependencies and python version for a python console', () => {
        const config = parseConsoleToml('[session.options]\npython_version = "3.13"\ndependencies = ["numpy", "pandas>=2"]');
        expect(config.pythonVersion).toBe('3.13');
        expect(config.dependencies).toEqual(['numpy', 'pandas>=2']);
    });

    it('parses repeated [[session.runner]] tables', () => {
        const toml = [
            '[[session.runner]]',
            'extensions = [".py", ".ipy"]',
            'command = \'%run "{script_path}"\'',
            '',
            '[[session.runner]]',
            'extensions = [".sh"]',
            'command = "bash {script_path}"',
        ].join('\n');

        const config = parseConsoleToml(toml);
        expect(config.runners).toEqual([
            { extensions: ['.py', '.ipy'], command: '%run "{script_path}"' },
            { extensions: ['.sh'], command: 'bash {script_path}' },
        ]);
    });

    it('parses repeated [[session.shortcut]] tables', () => {
        const toml = [
            '[[session.shortcut]]',
            'label = "Run tests"',
            'icon = "bs-play-fill"',
            'text = "pytest -q"',
        ].join('\n');

        const config = parseConsoleToml(toml);
        expect(config.shortcuts).toEqual([
            { label: 'Run tests', icon: 'bs-play-fill', text: 'pytest -q' },
        ]);
    });

    it('ignores blank lines, full-line comments, and inline comments', () => {
        const toml = [
            '# a leading comment',
            '[session]',
            'type = "shell"   # inline comment',
            '',
            '[session.options]',
            'executable = "bash"',
        ].join('\n');

        const config = parseConsoleToml(toml);
        expect(config.type).toBe('shell');
        expect(config.executable).toBe('bash');
    });

    it('preserves a # that sits inside a quoted value', () => {
        const config = parseConsoleToml('[session.environment]\nPROMPT = "a # b"');
        expect(config.environment.PROMPT).toBe('a # b');
    });

    it('unescapes quotes and backslashes in quoted values', () => {
        const config = parseConsoleToml('[session.options]\nexecutable = "C:\\\\Program Files\\\\pwsh.exe"');
        expect(config.executable).toBe('C:\\Program Files\\pwsh.exe');
    });

    it('returns a bare unquoted value verbatim', () => {
        const config = parseConsoleToml('[session]\ntype = shell');
        expect(config.type).toBe('shell');
    });

    it('throws on a non-section line with no equals sign', () => {
        expect(() => parseConsoleToml('[session]\ngarbage line')).toThrow();
    });

    it('throws when an array field holds a non-array value', () => {
        expect(() => parseConsoleToml('[session.options]\narguments = "-NoLogo"')).toThrow();
    });
});

describe('serializeConsoleToml', () => {
    it('omits empty optional fields', () => {
        const toml = serializeConsoleToml(defaultConsoleConfig());
        expect(toml).toContain('type = "shell"');
        expect(toml).not.toContain('executable');
        expect(toml).not.toContain('arguments');
        expect(toml).not.toContain('working_directory');
        expect(toml).not.toContain('dependencies');
        expect(toml).not.toContain('session.runner');
        expect(toml).not.toContain('session.shortcut');
    });

    it('quotes and comma-joins arguments', () => {
        const config = {
            ...defaultConsoleConfig(),
            executable: 'pwsh',
            arguments: ['-NoLogo', '-c', 'echo hi'],
        };
        const toml = serializeConsoleToml(config);
        expect(toml).toContain('arguments = ["-NoLogo", "-c", "echo hi"]');
    });

    it('emits runner and shortcut tables', () => {
        const config = {
            ...defaultConsoleConfig(),
            runners: [{ extensions: ['.py'], command: '%run "{script_path}"' }],
            shortcuts: [{ label: 'Test', icon: 'bs-play-fill', text: 'pytest' }],
        };
        const toml = serializeConsoleToml(config);
        expect(toml).toContain('[[session.runner]]');
        expect(toml).toContain('extensions = [".py"]');
        expect(toml).toContain('[[session.shortcut]]');
        expect(toml).toContain('label = "Test"');
        expect(toml).toContain('icon = "bs-play-fill"');
    });
});

describe('round-trip', () => {
    it('parse -> serialize -> parse is stable', () => {
        const original = {
            type: 'python',
            title: 'Python',
            executable: '',
            pythonVersion: '3.13',
            arguments: [],
            dependencies: ['numpy'],
            workingDirectory: 'tools',
            environment: { A: '1', B: 'two words' },
            runners: [{ extensions: ['.py', '.ipy'], command: '%run "{script_path}"' }],
            shortcuts: [{ label: 'Test', icon: 'bs-play-fill', text: 'pytest -q' }],
        };

        const once = parseConsoleToml(serializeConsoleToml(original));
        const twice = parseConsoleToml(serializeConsoleToml(once));

        expect(once).toEqual(original);
        expect(twice).toEqual(once);
    });
});
