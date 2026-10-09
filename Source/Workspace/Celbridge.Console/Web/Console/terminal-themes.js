// Adapted from the VS Code Dark+ and Light+ color schemes
// https://github.com/microsoft/vscode/blob/main/extensions/theme-defaults/themes/dark_plus.json
// https://github.com/microsoft/vscode/blob/main/extensions/theme-defaults/themes/light_plus.json
// The ANSI colors are VS Code's terminal defaults.
// https://github.com/microsoft/vscode/blob/main/src/vs/workbench/contrib/terminal/common/terminalColorRegistry.ts

const VSCodeDarkPlus = {
    foreground: '#D4D4D4',
    background: '#1E1E1E',
    cursor: '#FFFFFF',
    selectionBackground: 'rgba(255,255,255,0.18)', // subtle light highlight

    black: '#000000',
    red: '#cd3131',
    green: '#0dbc79',
    yellow: '#e5e510',
    blue: '#2472c8',
    magenta: '#bc3fbc',
    cyan: '#11a8cd',
    white: '#e5e5e5',

    brightBlack: '#666666',
    brightRed: '#f14c4c',
    brightGreen: '#23d18b',
    brightYellow: '#f5f543',
    brightBlue: '#3b8eea',
    brightMagenta: '#d670d6',
    brightCyan: '#29b8db',
    brightWhite: '#e5e5e5'
};

const VSCodeLightPlus = {
    foreground: '#333333',
    background: '#ffffff',
    cursor: '#333333',
    selectionBackground: 'rgba(0,0,0,0.13)', // subtle dark highlight

    black: '#000000',
    red: '#cd3131',
    green: '#107c10',
    yellow: '#949800',
    blue: '#0451a5',
    magenta: '#bc05bc',
    cyan: '#0598bc',
    white: '#555555',

    brightBlack: '#666666',
    brightRed: '#f14c4c',
    brightGreen: '#14ce14',
    brightYellow: '#b5ba00',
    brightBlue: '#3b8eea',
    brightMagenta: '#d670d6',
    brightCyan: '#29b8db',
    brightWhite: '#a5a5a5'
};

window.VSCodeTerminalThemes = {
    dark: VSCodeDarkPlus,
    light: VSCodeLightPlus
};
