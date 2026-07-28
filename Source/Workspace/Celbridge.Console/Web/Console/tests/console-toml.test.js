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
            executable: '',
            arguments: [],
            workingDirectory: '',
            environment: {},
        });
    });
});

describe('parseConsoleToml', () => {
    it('parses a full config across all sections', () => {
        const toml = [
            '[session]',
            'type = "shell"',
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
            executable: 'pwsh',
            arguments: ['-NoLogo', '-NoProfile'],
            workingDirectory: 'tools',
            environment: { BUILD_CONFIG: 'Debug' },
        });
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

    it('ignores unmodelled [session] keys such as title', () => {
        const config = parseConsoleToml('[session]\ntype = "shell"\ntitle = "Build"');
        expect(config.type).toBe('shell');
        expect(config).not.toHaveProperty('title');
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
});

describe('round-trip', () => {
    it('parse -> serialize -> parse is stable', () => {
        const original = {
            type: 'shell',
            executable: 'pwsh',
            arguments: ['-NoLogo'],
            workingDirectory: 'tools',
            environment: { A: '1', B: 'two words' },
        };

        const once = parseConsoleToml(serializeConsoleToml(original));
        const twice = parseConsoleToml(serializeConsoleToml(once));

        expect(once).toEqual(original);
        expect(twice).toEqual(once);
    });
});
