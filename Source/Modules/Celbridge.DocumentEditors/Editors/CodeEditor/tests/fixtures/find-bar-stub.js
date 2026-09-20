// Test stub for the shared find bar. The real module is served by the file server at
// /assets/celbridge-client/ui/find-bar.js; vitest aliases that URL to this file so
// preview-module.js can be imported under jsdom.

export function createFindBar() {
    return {
        open: () => false,
        refresh: () => {}
    };
}
