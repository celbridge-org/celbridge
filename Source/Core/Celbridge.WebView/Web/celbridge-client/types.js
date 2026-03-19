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

export const Types = {};
