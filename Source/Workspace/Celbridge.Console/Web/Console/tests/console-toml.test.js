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
            pythonVersion: '',
            arguments: [],
            dependencies: [],
            workingDirectory: '',
            startupScript: '',
            environment: {},
            runners: [],
            disabledBuiltInRunners: [],
            triggers: [],
            shortcuts: [],
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
            pythonVersion: '',
            arguments: ['-NoLogo', '-NoProfile'],
            dependencies: [],
            workingDirectory: 'tools',
            startupScript: '',
            environment: { BUILD_CONFIG: 'Debug' },
            runners: [],
            disabledBuiltInRunners: [],
            triggers: [],
            shortcuts: [],
        });
    });

    it('parses a startup script written as a multi-line block', () => {
        const toml = [
            '[session]',
            "startup_script = '''",
            'import numpy as np',
            '# not a comment inside the block',
            "'''",
        ].join('\n');
        expect(parseConsoleToml(toml).startupScript)
            .toBe('import numpy as np\n# not a comment inside the block');
    });

    it('parses a single-line startup script', () => {
        expect(parseConsoleToml('[session]\nstartup_script = "x = 1"').startupScript).toBe('x = 1');
    });

    it('throws on an unterminated block', () => {
        expect(() => parseConsoleToml("[session]\nstartup_script = '''\noops")).toThrow(/Unterminated/);
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
            'command = \'%run "{resource}"\'',
            '',
            '[[session.runner]]',
            'extensions = [".sh"]',
            'command = "bash {resource}"',
        ].join('\n');

        const config = parseConsoleToml(toml);
        expect(config.runners).toEqual([
            { extensions: ['.py', '.ipy'], command: '%run "{resource}"' },
            { extensions: ['.sh'], command: 'bash {resource}' },
        ]);
    });

    it('parses disabled_built_in_runners', () => {
        const config = parseConsoleToml('[session]\ntype = "python"\ndisabled_built_in_runners = ["python"]');
        expect(config.disabledBuiltInRunners).toEqual(['python']);
    });

    it('parses repeated [[session.trigger]] tables', () => {
        const toml = [
            '[[session.trigger]]',
            'pattern = "data/**/*.xlsx"',
            'command = "%run clean_data.py"',
            '',
            '[[session.trigger]]',
            'pattern = "*.py"',
            'command = \'%run "{resource}"\'',
        ].join('\n');

        const config = parseConsoleToml(toml);
        expect(config.triggers).toEqual([
            { pattern: 'data/**/*.xlsx', command: '%run clean_data.py' },
            { pattern: '*.py', command: '%run "{resource}"' },
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

    it('preserves a # that follows an escaped quote inside a value', () => {
        const config = parseConsoleToml('[[session.shortcut]]\nlabel = "Tag"\ntext = "echo \\"#tag\\""');
        expect(config.shortcuts[0].text).toBe('echo "#tag"');
    });

    it('splits array items on commas outside quotes only', () => {
        const config = parseConsoleToml('[session.options]\narguments = ["-c", "print(\\"a, b\\")"]');
        expect(config.arguments).toEqual(['-c', 'print("a, b")']);
    });

    it('keeps a comma inside a single-quoted array item', () => {
        const config = parseConsoleToml("[session.options]\narguments = ['a, b', 'c']");
        expect(config.arguments).toEqual(['a, b', 'c']);
    });

    it('parses a quoted environment key', () => {
        const config = parseConsoleToml('[session.environment]\n"A#B" = "x"\n"TWO WORDS" = "y"');
        expect(config.environment['A#B']).toBe('x');
        expect(config.environment['TWO WORDS']).toBe('y');
    });

    it('parses CRLF input', () => {
        const config = parseConsoleToml('[session]\r\ntype = "shell"\r\nworking_directory = "tools"\r\n');
        expect(config.type).toBe('shell');
        expect(config.workingDirectory).toBe('tools');
    });

    it('unescapes a trailing backslash without consuming the closing quote', () => {
        const config = parseConsoleToml('[session.options]\nexecutable = "C:\\\\tools\\\\"');
        expect(config.executable).toBe('C:\\tools\\');
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
        expect(toml).not.toContain('disabled_built_in_runners');
        // A section with no keys is as empty as an omitted key, so its header goes too.
        expect(toml).not.toContain('session.options');
        expect(toml).not.toContain('session.environment');
    });

    it('round-trips disabled_built_in_runners', () => {
        const config = { ...defaultConsoleConfig(), type: 'python', disabledBuiltInRunners: ['python'] };
        const toml = serializeConsoleToml(config);
        expect(toml).toContain('disabled_built_in_runners = ["python"]');
        expect(parseConsoleToml(toml).disabledBuiltInRunners).toEqual(['python']);
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

    it('emits a multi-line startup script as a literal block', () => {
        const config = { ...defaultConsoleConfig(), startupScript: 'import numpy as np\nx = 1' };
        const toml = serializeConsoleToml(config);
        expect(toml).toContain("startup_script = '''");
        expect(parseConsoleToml(toml).startupScript).toBe('import numpy as np\nx = 1');
    });

    it('emits a single-line startup script as a plain string', () => {
        const config = { ...defaultConsoleConfig(), startupScript: 'x = 1' };
        expect(serializeConsoleToml(config)).toContain('startup_script = "x = 1"');
    });

    it('emits runner and shortcut tables', () => {
        const config = {
            ...defaultConsoleConfig(),
            runners: [{ extensions: ['.py'], command: '%run "{resource}"' }],
            shortcuts: [{ label: 'Test', icon: 'bs-play-fill', text: 'pytest' }],
        };
        const toml = serializeConsoleToml(config);
        expect(toml).toContain('[[session.runner]]');
        expect(toml).toContain('extensions = [".py"]');
        expect(toml).toContain('[[session.shortcut]]');
        expect(toml).toContain('label = "Test"');
        expect(toml).toContain('icon = "bs-play-fill"');
    });

    it('emits trigger tables', () => {
        const config = {
            ...defaultConsoleConfig(),
            triggers: [{ pattern: '*.xlsx', command: '%run clean.py' }],
        };
        const toml = serializeConsoleToml(config);
        expect(toml).toContain('[[session.trigger]]');
        expect(toml).toContain('pattern = "*.xlsx"');
        expect(toml).toContain('command = "%run clean.py"');
    });
});

describe('round-trip', () => {
    it('parse -> serialize -> parse is stable', () => {
        const original = {
            type: 'python',
            executable: '',
            pythonVersion: '3.13',
            arguments: [],
            dependencies: ['numpy'],
            workingDirectory: 'tools',
            startupScript: 'import numpy as np\n%load_ext autoreload',
            environment: { A: '1', B: 'two words' },
            runners: [{ extensions: ['.py', '.ipy'], command: '%run "{resource}"' }],
            disabledBuiltInRunners: ['python'],
            triggers: [{ pattern: 'data/**/*.xlsx', command: '%run clean_data.py' }],
            shortcuts: [{ label: 'Test', icon: 'bs-play-fill', text: 'pytest -q' }],
        };

        const once = parseConsoleToml(serializeConsoleToml(original));
        const twice = parseConsoleToml(serializeConsoleToml(once));

        expect(once).toEqual(original);
        expect(twice).toEqual(once);
    });

    it('round-trips values built from hostile characters', () => {
        // Every value mixes the characters that exercise escaping, comment stripping, and array
        // splitting: double quote, backslash, hash, comma, single quote.
        const hostileValues = [
            'echo "#tag"',
            'a, "b, c", d',
            "single 'quoted' text",
            'C:\\path\\with\\backslashes\\',
            'mix \\" of # everything, \'here\'',
            '#leading hash',
            'trailing backslash \\',
        ];

        const original = {
            ...defaultConsoleConfig(),
            workingDirectory: hostileValues[0],
            executable: hostileValues[3],
            arguments: hostileValues,
            environment: { HOSTILE: hostileValues[4], 'ODD KEY#1': hostileValues[1] },
            runners: [{ extensions: ['.py'], command: hostileValues[4] }],
            shortcuts: [{ label: hostileValues[0], icon: 'bs-play-fill', text: hostileValues[1] }],
        };

        const once = parseConsoleToml(serializeConsoleToml(original));
        expect(once).toEqual(original);
    });
});
