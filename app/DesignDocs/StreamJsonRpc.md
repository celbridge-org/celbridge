# StreamJsonRpc for WebView2 Communication

## Status

| Phase | Description | Status |
|-------|-------------|--------|
| Phase 1 | Infrastructure | ✅ Complete |
| Phase 2 | Markdown Editor Migration | ✅ Complete |
| Phase 3 | Spreadsheet Editor Migration | ⏳ Pending |
| Phase 4 | Screenplay Editor Migration | ⏳ Pending |
| Phase 5 | Monaco Editor Migration | ⏳ Pending |
| Phase 6 | Cleanup | ⏳ Pending |

## Overview

This document outlines the transition from our custom JSON-RPC implementation (`CelbridgeHost`) to Microsoft's `StreamJsonRpc` library for WebView2 communication.

## Rationale

**Problems with current `CelbridgeHost` approach:**
- Ad-hoc JSON-RPC parsing, method dispatch, and error handling
- Scattered method definitions (`Method` constants on record types)
- Manual handler registration for each method
- Inconsistent with existing `StreamJsonRpc` usage for Python RPC (`RpcService.cs`)

**Benefits of StreamJsonRpc:**
- Battle-tested JSON-RPC 2.0 implementation
- Interface-based service contracts with `[JsonRpcMethod]` attributes
- Consistent patterns across the codebase
- Reduced boilerplate

## Requirements

### Functional Requirements

- **FR-1**: Support all existing RPC methods: `initialize`, `document/load`, `document/save`, `document/getMetadata`, `document/saveBinary`, `document/loadBinary`, `dialog/pickImage`, `dialog/pickFile`, `dialog/alert`
- **FR-2**: Support notifications: `document/changed`, `document/requestSave`, `document/externalChange`, `localization/updated`, `link/clicked`, `import/complete`
- **FR-3**: Maintain JSON-RPC 2.0 protocol compatibility with existing JavaScript clients
- **FR-4**: Preserve existing error handling behavior

### Non-Functional Requirements

- **NFR-1**: No changes to JavaScript client code during migration
- **NFR-2**: Migration should be incremental - existing views can migrate one at a time
- **NFR-3**: Maintain testability - the message channel abstraction should remain mockable

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Document View                            │
│  ┌─────────────────────────────────────────────────────┐    │
│  │   Service Implementations (IHostInit, IHostDocument) │    │
│  └─────────────────────────────────────────────────────┘    │
│                           │                                  │
│                           ▼                                  │
│  ┌─────────────────────────────────────────────────────┐    │
│  │          JsonRpc (StreamJsonRpc library)             │    │
│  └─────────────────────────────────────────────────────┘    │
│                           │                                  │
│                           ▼                                  │
│  ┌─────────────────────────────────────────────────────┐    │
│  │   HostRpcHandler (IJsonRpcMessageHandler + Channel)  │    │
│  └─────────────────────────────────────────────────────┘    │
│                           │                                  │
│                           ▼                                  │
│  ┌─────────────────────────────────────────────────────┐    │
│  │          IHostChannel / HostChannel                  │    │
│  └─────────────────────────────────────────────────────┘    │
└───────────────────────────┼─────────────────────────────────┘
                            ▼
                    ┌───────────────┐
                    │   WebView2    │
                    └───────────────┘
```

## Key Components

### Service Interfaces (in `Celbridge.UserInterface/Helpers/`)

| Interface | Methods | Purpose |
|-----------|---------|---------|
| `IHostInit` | `InitializeAsync` | Host initialization handshake |
| `IHostDocument` | `LoadAsync`, `SaveAsync`, `GetMetadataAsync`, `SaveBinaryAsync`, `LoadBinaryAsync` | Document operations |
| `IHostDialog` | `PickImageAsync`, `PickFileAsync`, `AlertAsync` | Dialog operations |
| `IHostNotifications` | `OnDocumentChanged`, `OnLinkClicked`, `OnImportComplete` | Notifications from JS |

### Infrastructure Components

| Component | Purpose |
|-----------|---------|
| `HostRpcHandler` | Bridges WebView2's push-based events to StreamJsonRpc's pull-based `ReadAsync()` using `Channel<T>`. Uses `SystemTextJsonFormatter` with camelCase naming for JavaScript interop. |
| `IHostChannel` / `HostChannel` | Abstraction over `CoreWebView2.PostWebMessageAsString`. Uses `DispatcherQueue` to marshal `PostMessage` calls to the UI thread. |
| `HostNotificationExtensions` | Extension methods for sending notifications to JS |
| `HostRpcMethods` | String constants for all RPC method names |

### Design Notes

- **Naming Convention**: Interfaces use `IHost` prefix to indicate host-side (C#) RPC services and avoid conflicts with existing `IDocumentService` and `IDialogService`.
- **Channel Buffering**: StreamJsonRpc's `IJsonRpcMessageHandler` uses pull-based `ReadAsync()`, while WebView2's `MessageReceived` is push-based. We bridge this using `Channel<T>`.
- **JSON Serialization**: Uses `SystemTextJsonFormatter` (not the Newtonsoft-based `JsonMessageFormatter`) configured with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` and `PropertyNameCaseInsensitive = true` for JavaScript interop.
- **Direct Interface Implementation**: Each document view implements the RPC interfaces directly (e.g., `MarkdownDocumentView : WebView2DocumentView, IHostInit, IHostDocument, IHostDialog, IHostNotifications`). This avoids creating separate handler classes that would just delegate back to the view.
- **Direct Method Parameters**: Interfaces use direct method parameters instead of wrapper record types. StreamJsonRpc automatically maps JSON-RPC named parameters to method parameters. Record types are only used for results that bundle multiple values.
- **UI Thread Dispatching**: Set `JsonRpc.SynchronizationContext` to the UI thread's `SynchronizationContext.Current` so that all RPC method handlers run on the UI thread. This allows handlers to safely show dialogs and interact with UI elements.

## Usage Pattern (After Migration)

Each editor view implements the service interfaces directly and registers itself with `JsonRpc`:

```csharp
// In document view that implements IHostInit, IHostDocument, IHostDialog, IHostNotifications
var channel = new HostChannel(WebView.CoreWebView2);
var handler = new HostRpcHandler(channel);
var rpc = new JsonRpc(handler);

// Ensure RPC method handlers run on the UI thread
rpc.SynchronizationContext = SynchronizationContext.Current;

// Register this view as the handler for all RPC interfaces
rpc.AddLocalRpcTarget<IHostInit>(this, null);
rpc.AddLocalRpcTarget<IHostDocument>(this, null);
rpc.AddLocalRpcTarget<IHostDialog>(this, null);
rpc.AddLocalRpcTarget<IHostNotifications>(this, null);

rpc.StartListening();
```

## Implementation Plan

### Phase 1: Infrastructure ✅ Complete

**Goal**: Create the core infrastructure without changing existing views.

**Deliverables**:
- `HostRpcHandler.cs` - Custom `IJsonRpcMessageHandler` with `Channel<T>` buffering
- `IHostChannel.cs` / `HostChannel.cs` - Renamed from `IWebViewMessageChannel` / `WebView2MessageChannel`
- Service interfaces: `IHostInit.cs`, `IHostDocument.cs`, `IHostDialog.cs`, `IHostNotifications.cs`
- `HostNotificationExtensions.cs` - Extension methods for outbound notifications
- `HostRpcMethods.cs` - Centralized method name constants
- Updated `CelbridgeHostTypes.cs` - Removed `Method` constants (pure data types)
- `HostRpcHandlerTests.cs` - Unit tests for the RPC handler

### Phase 2: Markdown Editor Migration (Pilot) ✅ Complete

**Goal**: Migrate the Markdown editor as a pilot to validate the approach.

**Tasks**:
1. Update `MarkdownDocumentView` to implement `IHostInit`, `IHostDocument`, `IHostDialog`, `IHostNotifications` directly
2. Replace `CelbridgeHost` with `JsonRpc` and register `this` as the RPC target
3. Verify all existing functionality works

**Deliverables**:
- Updated `MarkdownDocumentView.xaml.cs` implementing all RPC interfaces directly

**Design Decision**: Rather than creating separate handler classes that delegate back to the view, each view implements the RPC interfaces directly. This eliminates boilerplate and keeps the handler logic where it belongs.

### Phase 3: Spreadsheet Editor Migration

**Goal**: Migrate the Spreadsheet editor, validating binary content handling.

**Tasks**:
1. Update `SpreadsheetDocumentView` to implement `IHostInit`, `IHostDocument`, `IHostDialog`, `IHostNotifications`
2. Replace `CelbridgeHost` with `JsonRpc` and register `this` as the RPC target
3. Verify binary load/save functionality (`SaveBinaryAsync`, `LoadBinaryAsync`)

**Deliverables**:
- Updated `SpreadsheetDocumentView.xaml.cs` implementing all RPC interfaces directly

### Phase 4: Screenplay Editor Migration

**Goal**: Migrate the Screenplay scene editor.

**Tasks**:
1. Update `SceneDocumentView` to implement `IHostInit`, `IHostDocument`, `IHostDialog`, `IHostNotifications`
2. Replace `CelbridgeHost` with `JsonRpc` and register `this` as the RPC target
3. Verify all existing functionality

**Deliverables**:
- Updated `SceneDocumentView.xaml.cs` implementing all RPC interfaces directly

### Phase 5: Monaco Editor Migration

**Goal**: Migrate the Monaco code editor.

**Tasks**:
1. Update `MonacoEditorView` to implement `IHostInit`, `IHostDocument`, `IHostDialog`, `IHostNotifications`
2. Replace `CelbridgeHost` with `JsonRpc` and register `this` as the RPC target
3. Verify all existing functionality

**Deliverables**:
- Updated `MonacoEditorView.cs` implementing all RPC interfaces directly

### Phase 6: Cleanup

**Goal**: Remove deprecated code and finalize migration.

**Tasks**:
1. Delete `CelbridgeHost.cs`
2. Delete obsolete files (`WebView2Messenger.cs`, etc.)
3. Update documentation
4. Final testing pass

**Deliverables**:
- Removed deprecated files
- Updated documentation

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| StreamJsonRpc may not support WebView2's message-based transport natively | High | Create custom `IJsonRpcMessageHandler` implementation ✅ Done |
| Method naming conventions may differ | Medium | Use `[JsonRpcMethod]` attributes to specify exact method names |
| JavaScript client compatibility | High | Maintain JSON-RPC 2.0 protocol compatibility; no JS changes in Phase 1-5 |

## Testing Strategy

- **Unit tests**: `HostRpcHandlerTests` tests the RPC handler with `MockHostChannel`
- **Integration tests**: Full round-trip communication with test WebView2 instances
- **Manual testing**: Verify all document editors function correctly after migration
- **Regression testing**: Run existing test suites to ensure no functionality breaks

## References

- [StreamJsonRpc GitHub](https://github.com/microsoft/vs-streamjsonrpc)
- [JSON-RPC 2.0 Specification](https://www.jsonrpc.org/specification)
- Existing usage: `Workspace/Celbridge.Python/Services/RpcService.cs`
