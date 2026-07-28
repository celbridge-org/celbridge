// Adapted from the VS Code Dark+ and Light+ color schemes
// https://github.com/microsoft/vscode/blob/main/extensions/theme-defaults/themes/dark_plus.json
// https://github.com/microsoft/vscode/blob/main/extensions/theme-defaults/themes/light_plus.json

const VSCodeDarkPlus = {
    foreground: '#D4D4D4',
    background: '#1E1E1E',
    cursor: '#FFFFFF',
    selectionBackground: 'rgba(255,255,255,0.18)', // subtle light highlight

    black: '#000000',
    red: '#cd3131',
    green: '#6a9955',
    yellow: '#CE9178',
    blue: '#2472c8',
    magenta: '#C586C0',
    cyan: '#11a8cd',
    white: '#e5e5e5',

    brightBlack: '#666666',
    brightRed: '#f14c4c',
    brightGreen: '#b5cea8',
    brightYellow: '#DCDCAA',
    brightBlue: '#569cd6',
    brightMagenta: '#daadd6',
    brightCyan: '#9cdcfe',
    brightWhite: '#e5e5e5'
};

const VSCodeLightPlus = {
    foreground: '#333333',
    background: '#ffffff',
    cursor: '#333333',
    selectionBackground: 'rgba(0,0,0,0.13)', // subtle dark highlight

    black: '#000000',
    red: '#e51400',
    green: '#0f9d58',
    yellow: '#b36b00',
    blue: '#0451a5',
    magenta: '#a315a8',
    cyan: '#008cba',
    white: '#ffffff',

    brightBlack: '#666666',
    brightRed: '#f14c4c',
    brightGreen: '#89d88b',
    brightYellow: '#ffea00',
    brightBlue: '#4e8ae9',
    brightMagenta: '#d16abc',
    brightCyan: '#9cdcfe',
    brightWhite: '#f7f7f7'
};

window.VSCodeTerminalThemes = {
    dark: VSCodeDarkPlus,
    light: VSCodeLightPlus
};
