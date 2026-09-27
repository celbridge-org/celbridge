// Test stub for the shared find bar. The real module is served by the file server at
// /assets/celbridge-client/ui/find-bar.js; vitest aliases that URL to this file so
// preview-module.js can be imported under jsdom.

// Every find bar created, oldest first. Each records its options and how often it was opened.
export const __createdFindBars = [];

export function createFindBar(options) {
    const findBar = {
        options,
        openCount: 0,
        open() {
            findBar.openCount++;
            return true;
        },
        refresh: () => {}
    };
    __createdFindBars.push(findBar);

    return findBar;
}
