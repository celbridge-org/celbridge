# Celbridge WebView Bridge

This directory contains the JavaScript client library for communication between web-based editors and the Celbridge host application.

## Running Tests Locally

### Prerequisites

- [Node.js](https://nodejs.org/) (v18 or later recommended)

### Setup

Navigate to this directory and install dependencies:

```bash
cd Core/Celbridge.UserInterface/Web
npm install
```

### Run Tests

Run tests once:

```bash
npm test
```

Run tests in watch mode (re-runs on file changes):

```bash
npm run test:watch
```

## Files

- `celbridge.js` - The client library for WebView-to-host communication
- `celbridge-localization.js` - Localization utilities for web-based editors
- `types.js` - Type definitions
- `celbridge.test.js` - Unit tests for the client library
- `vitest.config.js` - Vitest test runner configuration
