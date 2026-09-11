// The rule deciding when a size the session reports is one the terminal can take.

/**
 * Whether a size the session reports is one to resize the terminal to. Anything but a positive whole number
 * of cells is not a size a terminal has, and the size it is already at is not worth taking.
 * @param {unknown} cols
 * @param {unknown} rows
 * @param {number} currentCols
 * @param {number} currentRows
 * @returns {boolean}
 */
export function isAdoptableSize(cols, rows, currentCols, currentRows) {
    if (!Number.isInteger(cols) ||
        !Number.isInteger(rows) ||
        cols <= 0 ||
        rows <= 0) {
        return false;
    }

    return cols !== currentCols || rows !== currentRows;
}
