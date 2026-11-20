// The code editor's Monaco themes. Each theme adds colors for a language server's semantic tokens to one of
// Monaco's own themes. A semantic token in the page has one of four types, and each type has one color.
//
// The module expects the global `monaco` AMD namespace to be loaded before any function is called.

// The token types of the page's semantic tokens. The themes' rules match these names alone, so the rules leave the
// colors of every other token unchanged.
export const SemanticTokenType = Object.freeze({
    Type: 'semantic-type',
    Function: 'semantic-function',
    Variable: 'semantic-variable',
    Constant: 'semantic-constant'
});

// The page's legend for semantic tokens. Monaco reads the page's tokens with this legend. The page's tokens have
// no modifiers.
export const semanticTokenLegend = Object.freeze({
    tokenTypes: Object.values(SemanticTokenType),
    tokenModifiers: []
});

// The colors of VS Code's Light+ and Dark+ themes for types, functions, variables and constants.
const lightColors = {
    [SemanticTokenType.Type]: '267F99',
    [SemanticTokenType.Function]: '795E26',
    [SemanticTokenType.Variable]: '001080',
    [SemanticTokenType.Constant]: '0070C1'
};

const darkColors = {
    [SemanticTokenType.Type]: '4EC9B0',
    [SemanticTokenType.Function]: 'DCDCAA',
    [SemanticTokenType.Variable]: '9CDCFE',
    [SemanticTokenType.Constant]: '4FC1FF'
};

const lightThemeName = 'celbridge-light';
const darkThemeName = 'celbridge-dark';

/**
 * Defines the editor's light and dark themes with Monaco.
 */
export function defineEditorThemes() {
    monaco.editor.defineTheme(lightThemeName, {
        base: 'vs',
        inherit: true,
        rules: toRules(lightColors),
        colors: {}
    });

    monaco.editor.defineTheme(darkThemeName, {
        base: 'vs-dark',
        inherit: true,
        rules: toRules(darkColors),
        colors: {}
    });
}

/**
 * Returns the name of the editor's theme for the application's theme.
 * @param {string} appTheme - The application's theme, 'Light' or 'Dark'.
 */
export function getEditorThemeName(appTheme) {
    return appTheme === 'Dark' ? darkThemeName : lightThemeName;
}

function toRules(colors) {
    return Object.values(SemanticTokenType).map((tokenType) => ({ token: tokenType, foreground: colors[tokenType] }));
}
