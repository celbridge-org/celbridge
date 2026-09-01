// Recommended correctness rules only. Formatting is owned by .editorconfig, so no
// stylistic rules are configured here.

import js from '@eslint/js';
import globals from 'globals';

// Third party code we vendor rather than author: xterm and TipTap bundles under
// lib/, the Monaco distribution under min/vs/, and the project templates.
const vendoredPaths = [
    '**/node_modules/**',
    '**/bin/**',
    '**/obj/**',
    '**/lib/**',
    '**/min/vs/**',
    '**/fixtures/**',
    '**/.venv/**',
    'Templates/**'
];

// Build scripts and Vitest configuration run under Node, everything else in a WebView.
const nodeScripts = [
    '**/build/vendor.js',
    '**/vitest.config.js'
];

const testScripts = [
    '**/tests/**/*.js'
];

export default [
    {
        ignores: vendoredPaths
    },

    js.configs.recommended,

    {
        rules: {
            // The shim rethrows without attaching the caught error as a cause, which is
            // the convention here.
            'preserve-caught-error': 'off'
        }
    },

    {
        languageOptions: {
            ecmaVersion: 'latest',
            sourceType: 'module',
            globals: globals.browser
        }
    },

    {
        files: nodeScripts,
        languageOptions: {
            globals: globals.node
        }
    },

    {
        // Tests exercise browser code from a Node process, so both sets apply.
        files: testScripts,
        languageOptions: {
            globals: {
                ...globals.browser,
                ...globals.node
            }
        }
    },

    {
        // Injected into the page as a classic script rather than imported as a module.
        files: ['Core/Celbridge.WebHost/Web/celbridge-client/core/webview-tools-shim.js'],
        languageOptions: {
            sourceType: 'script'
        }
    },

    {
        // Monaco and its AMD loader both arrive from script tags on the editor page.
        files: ['Modules/Celbridge.DocumentEditors/Editors/CodeEditor/**/*.js'],
        languageOptions: {
            globals: {
                monaco: 'readonly',
                require: 'readonly'
            }
        }
    },

    {
        // SpreadJS publishes its whole API under the GC namespace from a script tag.
        files: ['Modules/Celbridge.Spreadsheet/Package/**/*.js'],
        languageOptions: {
            globals: {
                GC: 'readonly'
            }
        }
    },

    {
        // xterm and its addons are loaded from script tags in the console page.
        files: ['Workspace/Celbridge.Console/Web/Console/**/*.js'],
        languageOptions: {
            globals: {
                Terminal: 'readonly',
                FitAddon: 'readonly',
                ClipboardAddon: 'readonly',
                Unicode11Addon: 'readonly',
                WebLinksAddon: 'readonly'
            }
        }
    }
];
