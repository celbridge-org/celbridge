# Celbridge Client SDK

JavaScript client SDK for communicating with the Celbridge .NET host via JSON-RPC.

## Running Tests

To run the unit tests for the celbridge-client SDK:

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
├── celbridge.js          # Main SDK entry point
├── localization.js       # Localization utilities for WebView editors
├── types.js              # JSDoc type definitions
├── api/                  # API modules
│   ├── code-preview-api.js  # Code preview operations
│   ├── dialog-api.js     # Dialog operations
│   ├── document-api.js   # Document operations
│   ├── localization-api.js
│   └── theme-api.js      # Theme events
├── core/
│   └── rpc-transport.js  # JSON-RPC 2.0 transport layer
└── tests/
    └── celbridge.test.js # Unit tests
```
