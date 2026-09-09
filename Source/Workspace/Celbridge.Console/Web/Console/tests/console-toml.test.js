import { describe, it, expect } from 'vitest';
import {
    defaultConsoleConfig,
    parseConsoleToml,
    serializeConsoleToml,
} from '../console-toml.js';

// The session types the client has modules for, as console.js passes them. A [session.<type>] table named
// for anything else is not carried, so the tests name the same set the settings form offers.
const SESSION_TYPE_IDS = ['shell', 'python'];

function parse(text) {
    return parseConsoleToml(text, SESSION_TYPE_IDS);
}

describe('defaultConsoleConfig', () => {
    it('returns a blank shell config', () => {
        expect(defaultConsoleConfig()).toEqual({
            type: 'shell',
            workingDirectory: '',
            optionsBySessionType: {},
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
            '[session.environment]',
            'BUILD_CONFIG = "Debug"',
            '',
            '[session.shell]',
            'executable = "pwsh"',
            'arguments = ["-NoLogo", "-NoProfile"]',
        ].join('\n');

        expect(parse(toml)).toEqual({
            type: 'shell',
            workingDirectory: 'tools',
            optionsBySessionType: {
                shell: { executable: 'pwsh', arguments: ['-NoLogo', '-NoProfile'] },
            },
            environment: { BUILD_CONFIG: 'Debug' },
            runners: [],
            disabledBuiltInRunners: [],
            triggers: [],
            shortcuts: [],
        });
    });

    it('keeps the table of a type that is not selected', () => {
        const toml = [
            '[session]',
            'type = "shell"',
            '',
            '[session.shell]',
            'executable = "bash"',
            '',
            '[session.python]',
            'dependencies = ["numpy"]',
        ].join('\n');

        expect(parse(toml).optionsBySessionType).toEqual({
            shell: { executable: 'bash' },
            python: { dependencies: ['numpy'] },
        });
    });

    it('drops a table named for a type this client cannot edit', () => {
        const toml = [
            '[session]',
            'type = "shell"',
            '',
            '[session.shell]',
            'executable = "bash"',
            '',
            '[session.options]',
            'python_version = "3.13"',
        ].join('\n');

        const config = parse(toml);

        expect(config.optionsBySessionType).toEqual({ shell: { executable: 'bash' } });
        expect(serializeConsoleToml(config)).not.toContain('session.options');
    });

    it('parses a script written as a multi-line block', () => {
        const toml = [
            '[session.shell]',
            "script = '''",
            'import numpy as np',
            '# not a comment inside the block',
            "'''",
        ].join('\n');
        expect(parse(toml).optionsBySessionType.shell.script)
            .toBe('import numpy as np\n# not a comment inside the block');
    });

    it('parses a single-line script', () => {
        expect(parse('[session.shell]\nscript = "x = 1"').optionsBySessionType.shell.script).toBe('x = 1');
    });

    it('throws on an unterminated block', () => {
        expect(() => parse("[session.shell]\nscript = '''\noops")).toThrow(/Unterminated/);
    });

    it('parses dependencies and python version for a python console', () => {
        const config = parse('[session.python]\npython_version = "3.13"\ndependencies = ["numpy", "pandas>=2"]');
        expect(config.optionsBySessionType.python.python_version).toBe('3.13');
        expect(config.optionsBySessionType.python.dependencies).toEqual(['numpy', 'pandas>=2']);
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

        const config = parse(toml);
        expect(config.runners).toEqual([
            { extensions: ['.py', '.ipy'], command: '%run "{resource}"' },
            { extensions: ['.sh'], command: 'bash {resource}' },
        ]);
    });

    it('parses disabled_runners', () => {
        const config = parse('[session]\ntype = "python"\ndisabled_runners = ["python"]');
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

        const config = parse(toml);
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

        const config = parse(toml);
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
            '[session.shell]',
            'executable = "bash"',
        ].join('\n');

        const config = parse(toml);
        expect(config.type).toBe('shell');
        expect(config.optionsBySessionType.shell.executable).toBe('bash');
    });

    it('preserves a # that sits inside a quoted value', () => {
        const config = parse('[session.environment]\nPROMPT = "a # b"');
        expect(config.environment.PROMPT).toBe('a # b');
    });

    it('preserves a # that follows an escaped quote inside a value', () => {
        const config = parse('[[session.shortcut]]\nlabel = "Tag"\ntext = "echo \\"#tag\\""');
        expect(config.shortcuts[0].text).toBe('echo "#tag"');
    });

    it('splits array items on commas outside quotes only', () => {
        const config = parse('[session.shell]\narguments = ["-c", "print(\\"a, b\\")"]');
        expect(config.optionsBySessionType.shell.arguments).toEqual(['-c', 'print("a, b")']);
    });

    it('keeps a comma inside a single-quoted array item', () => {
        const config = parse("[session.shell]\narguments = ['a, b', 'c']");
        expect(config.optionsBySessionType.shell.arguments).toEqual(['a, b', 'c']);
    });

    it('parses a quoted environment key', () => {
        const config = parse('[session.environment]\n"A#B" = "x"\n"TWO WORDS" = "y"');
        expect(config.environment['A#B']).toBe('x');
        expect(config.environment['TWO WORDS']).toBe('y');
    });

    it('parses CRLF input', () => {
        const config = parse('[session]\r\ntype = "shell"\r\nworking_directory = "tools"\r\n');
        expect(config.type).toBe('shell');
        expect(config.workingDirectory).toBe('tools');
    });

    it('unescapes a trailing backslash without consuming the closing quote', () => {
        const config = parse('[session.shell]\nexecutable = "C:\\\\tools\\\\"');
        expect(config.optionsBySessionType.shell.executable).toBe('C:\\tools\\');
    });

    it('unescapes quotes and backslashes in quoted values', () => {
        const config = parse('[session.shell]\nexecutable = "C:\\\\Program Files\\\\pwsh.exe"');
        expect(config.optionsBySessionType.shell.executable).toBe('C:\\Program Files\\pwsh.exe');
    });

    it('returns a bare unquoted value verbatim', () => {
        const config = parse('[session]\ntype = shell');
        expect(config.type).toBe('shell');
    });

    it('throws on a non-section line with no equals sign', () => {
        expect(() => parse('[session]\ngarbage line')).toThrow();
    });

    it('throws on an unterminated array in a type table', () => {
        expect(() => parse('[session.shell]\narguments = ["-NoLogo"')).toThrow();
    });
});

describe('serializeConsoleToml', () => {
    it('omits empty optional fields', () => {
        const toml = serializeConsoleToml(defaultConsoleConfig());
        expect(toml).toContain('type = "shell"');
        expect(toml).not.toContain('working_directory');
        expect(toml).not.toContain('session.runner');
        expect(toml).not.toContain('session.shortcut');
        expect(toml).not.toContain('disabled_runners');
        // A section with no keys is as empty as an omitted key, so its header goes too.
        expect(toml).not.toContain('session.shell');
        expect(toml).not.toContain('session.environment');
    });

    it('omits a type table whose keys are all empty', () => {
        const config = { ...defaultConsoleConfig(), optionsBySessionType: { shell: { executable: '', arguments: [] } } };
        expect(serializeConsoleToml(config)).not.toContain('session.shell');
    });

    it('writes the table of a type that is not selected', () => {
        const config = {
            ...defaultConsoleConfig(),
            type: 'shell',
            optionsBySessionType: { shell: { executable: 'bash' }, python: { dependencies: ['numpy'] } },
        };
        const toml = serializeConsoleToml(config);
        expect(toml).toContain('[session.shell]');
        expect(toml).toContain('[session.python]');
        expect(parse(toml).optionsBySessionType.python.dependencies).toEqual(['numpy']);
    });

    it('round-trips disabled_runners', () => {
        const config = { ...defaultConsoleConfig(), type: 'python', disabledBuiltInRunners: ['python'] };
        const toml = serializeConsoleToml(config);
        expect(toml).toContain('disabled_runners = ["python"]');
        expect(parse(toml).disabledBuiltInRunners).toEqual(['python']);
    });

    it('quotes and comma-joins arguments', () => {
        const config = {
            ...defaultConsoleConfig(),
            optionsBySessionType: { shell: { executable: 'pwsh', arguments: ['-NoLogo', '-c', 'echo hi'] } },
        };
        const toml = serializeConsoleToml(config);
        expect(toml).toContain('arguments = ["-NoLogo", "-c", "echo hi"]');
    });

    it('emits a multi-line script as a literal block', () => {
        const config = {
            ...defaultConsoleConfig(),
            optionsBySessionType: { shell: { script: 'import numpy as np\nx = 1' } },
        };
        const toml = serializeConsoleToml(config);
        expect(toml).toContain("script = '''");
        expect(parse(toml).optionsBySessionType.shell.script).toBe('import numpy as np\nx = 1');
    });

    it('emits a single-line script as a plain string', () => {
        const config = { ...defaultConsoleConfig(), optionsBySessionType: { shell: { script: 'x = 1' } } };
        expect(serializeConsoleToml(config)).toContain('script = "x = 1"');
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
    it('round-trips a script holding the block delimiter', () => {
        // The literal block cannot carry ''' so the value falls back to an escaped basic string, which has
        // to escape its line breaks: a raw newline inside one is not valid TOML on either parser.
        const script = "echo a\n'''\necho b";
        const config = defaultConsoleConfig();
        config.optionsBySessionType = { shell: { script } };

        const toml = serializeConsoleToml(config);

        expect(toml).toContain('script = "echo a\\n\'\'\'\\necho b"');
        expect(parse(toml).optionsBySessionType.shell.script).toBe(script);
    });

    it('parse -> serialize -> parse is stable', () => {
        const original = {
            type: 'python',
            workingDirectory: 'tools',
            optionsBySessionType: {
                python: {
                    python_version: '3.13',
                    dependencies: ['numpy'],
                    script: 'import numpy as np\n%load_ext autoreload',
                },
                shell: { executable: 'pwsh' },
            },
            environment: { A: '1', B: 'two words' },
            runners: [{ extensions: ['.py', '.ipy'], command: '%run "{resource}"' }],
            disabledBuiltInRunners: ['python'],
            triggers: [{ pattern: 'data/**/*.xlsx', command: '%run clean_data.py' }],
            shortcuts: [{ label: 'Test', icon: 'bs-play-fill', text: 'pytest -q' }],
        };

        const once = parse(serializeConsoleToml(original));
        const twice = parse(serializeConsoleToml(once));

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
            optionsBySessionType: { shell: { executable: hostileValues[3], arguments: hostileValues } },
            environment: { HOSTILE: hostileValues[4], 'ODD KEY#1': hostileValues[1] },
            runners: [{ extensions: ['.py'], command: hostileValues[4] }],
            shortcuts: [{ label: hostileValues[0], icon: 'bs-play-fill', text: hostileValues[1] }],
        };

        const once = parse(serializeConsoleToml(original));
        expect(once).toEqual(original);
    });
});
