// Test stub for the celbridge client module. The real module is served by
// the WebView host at https://shared.celbridge/...; vitest aliases that URL
// to this file so editor-controller.js can be imported under jsdom without
// the live host environment.

export const __capturedHandlers = {};

const celbridge = {
    options: {},
    document: {
        notifyChanged: () => {},
        notifyContentLoaded: () => {},
        notifyClientReady: () => {},
        save: async () => {},
        load: async () => ({})
    },
    input: {
        notifyLinkClicked: () => {}
    },
    initializeDocument: async (handlers) => {
        Object.assign(__capturedHandlers, handlers);
    },
    onNotification: () => {}
};

export default celbridge;
