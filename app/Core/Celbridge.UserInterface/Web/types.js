// Celbridge API Type Definitions
// JSDoc typedefs for the Celbridge client API.

/**
 * @typedef {Object} DocumentMetadata
 * @property {string} filePath - Full path to the file on disk.
 * @property {string} resourceKey - The resource key in the project.
 * @property {string} fileName - The file name only.
 */

/**
 * @typedef {'Light' | 'Dark'} WebViewTheme
 */

/**
 * @typedef {Object} InitializeResult
 * @property {string} content - The document content.
 * @property {DocumentMetadata} metadata - Document metadata.
 * @property {Object<string, string>} localization - Localized strings dictionary.
 */

/**
 * @typedef {Object} LoadResult
 * @property {string} content - The document content from disk.
 * @property {DocumentMetadata} [metadata] - Optional metadata if requested.
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
