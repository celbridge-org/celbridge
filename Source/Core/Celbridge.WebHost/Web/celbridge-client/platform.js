// Identifies the operating system the page's web view runs on, for the places the platforms differ. The
// client hints are read first, then the older platform string, then the user agent, as each is missing on
// some web views.

function platformDescription() {
    return navigator.userAgentData?.platform ||
        navigator.platform ||
        navigator.userAgent ||
        '';
}

/**
 * Whether the page runs on macOS.
 */
export function isMacOS() {
    return /mac/i.test(platformDescription());
}

/**
 * Whether the page runs on Windows.
 */
export function isWindows() {
    return /windows|win32|win64/i.test(platformDescription());
}
