// Test stub for the celbridge client module. The real module is served by
// the file server at /assets/celbridge-client/...; vitest aliases that URL
// to this file so editor-controller.js can be imported under jsdom without
// the live host environment.

export const __capturedHandlers = {};

// Every availability report the editor sends, newest last.
export const __capturedEditAvailability = [];

const celbridge = {
    options: {},
    // Reports to the host are gated on this, so a test opts in before asserting on them.
    isHosted: false,
    appState: {
        current: { theme: 'Light' },
        onChanged: (handler) => { __capturedHandlers.onAppStateChanged = handler; }
    },
    viewState: {
        current: {},
        onChanged: (handler) => { __capturedHandlers.onViewStateChanged = handler; }
    },
    document: {
        notifyChanged: () => {},
        notifyContentLoaded: () => {},
        save: async () => {},
        load: async () => ({})
    },
    input: {
        notifyLinkClicked: () => {},
        notifyEditAvailability: (availability) => { __capturedEditAvailability.push(availability); }
    },
    initializeDocument: async (handlers) => {
        Object.assign(__capturedHandlers, handlers);
    },
    onNotification: () => {}
};

export default celbridge;
