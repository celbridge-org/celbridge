// Celbridge API Type Definitions
// JSDoc typedefs for the Celbridge client API.

/**
 * @typedef {Object} DocumentMetadata
 * @property {string} filePath - Full path to the file on disk.
 * @property {string} resourceKey - The resource key in the project.
 * @property {string} fileName - The file name only.
 * @property {string} locale - The UI locale (e.g., "en", "fr") for localization loading.
 */

/**
 * @typedef {'Light' | 'Dark'} WebViewTheme
 */

/**
 * @typedef {Object} InitializeResult
 * @property {string} content - The document content.
 * @property {DocumentMetadata} metadata - Document metadata including locale.
 */

/**
 * @typedef {Object} LoadResult
 * @property {string} content - The document content from disk (text or base64 for binary).
 * @property {DocumentMetadata} metadata - Document metadata.
 */

/**
 * @typedef {Object} SaveResult
 * @property {boolean} success - Whether the save succeeded.
 * @property {string} [error] - Error message if save failed.
 */

/**
 * @typedef {'none' | 'error' | 'warn' | 'debug'} LogLevel
 */

/**
 * The kind of state a status pip marks. The pip shows each kind in its own color.
 * @typedef {'neutral' | 'success' | 'notice' | 'alert'} PipKind
 */

/**
 * The root and the filters of a watch on files. The filters are relative to the root, and follow the search filters
 * of the file tools.
 * @typedef {Object} WatchFilesOptions
 * @property {string} [root] - The root to watch, as a root key: 'project:', 'temp:' or 'utils:'. Defaults to
 *   'project:'.
 * @property {string} [include] - The patterns of the paths to follow, separated by commas. Defaults to every path.
 * @property {string} [exclude] - The patterns of the paths to leave out, separated by commas. Defaults to none.
 */

/**
 * The item at a key when a change is delivered: a file, a folder or nothing.
 * @typedef {'file' | 'folder' | 'absent'} FileChangeState
 */

/**
 * One change to a key. The change gives the key and the item at the key when the change is delivered.
 * @typedef {Object} FileChange
 * @property {string} resource - The resource key, with the root's prefix, like 'project:src/main.py'.
 * @property {FileChangeState} state - The item at the key.
 */

export const Types = {};
