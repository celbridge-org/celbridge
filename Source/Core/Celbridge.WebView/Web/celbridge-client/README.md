# Celbridge Client

JavaScript client for communicating with the Celbridge .NET host via JSON-RPC.

## Running Tests

To run the unit tests for the celbridge-client:

```bash
cd Core/Celbridge.WebView/Web/celbridge-client
npm install
npm test
```

To run tests in watch mode during development:

```bash
npm run test:watch
```

## Project Structure

```
celbridge-client/
├── celbridge.js          # Main client entry point
├── localization.js       # Localization utilities for WebView editors
├── types.js              # JSDoc type definitions
├── api/                  # API modules
│   ├── dialog-api.js     # Dialog operations
│   ├── document-api.js   # Document operations
│   ├── input-api.js      # Input events (keyboard, link clicks, scroll)
│   ├── localization-api.js
│   └── theme-api.js      # Theme events
├── core/
│   └── rpc-transport.js  # JSON-RPC 2.0 transport layer
└── tests/
    └── celbridge.test.js # Unit tests
```
