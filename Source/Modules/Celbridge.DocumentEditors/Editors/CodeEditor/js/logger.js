// Tiny lifecycle logger for the Monaco editor WebView.
// All messages share a consistent prefix so they're easy to filter in the
// WebView2 dev tools console. Keep log sites narrow: lifecycle transitions and
// scroll/state restore paths, not per-keystroke or per-scroll-event fire hoses.
//
// We bind console.log/warn rather than wrap them in a function so DevTools
// reports each call site's original file:line (a wrapper function would pin
// every message to this module instead).

const PREFIX = '[monaco]';

export const log = console.log.bind(console, PREFIX);
export const warn = console.warn.bind(console, PREFIX);
