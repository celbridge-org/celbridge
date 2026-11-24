using System.Text.Json.Nodes;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// The language client's capabilities, declared to every server. The registration of watched files is absent, so
/// a server watches the project's files itself.
/// </summary>
internal static class ClientCapabilities
{
    private const string CapabilitiesJson = """
        {
          "general": {
            "positionEncodings": ["utf-16"]
          },
          "window": {
            "workDoneProgress": false
          },
          "workspace": {
            "applyEdit": true,
            "workspaceEdit": {
              "documentChanges": true
            },
            "configuration": true,
            "workspaceFolders": true,
            "diagnostics": {
              "refreshSupport": true
            }
          },
          "textDocument": {
            "synchronization": {
              "dynamicRegistration": false,
              "didSave": false
            },
            "publishDiagnostics": {
              "relatedInformation": true,
              "tagSupport": { "valueSet": [1, 2] }
            },
            "diagnostic": {
              "dynamicRegistration": false,
              "relatedDocumentSupport": false
            },
            "completion": {
              "contextSupport": true,
              "completionItem": {
                "snippetSupport": true,
                "commitCharactersSupport": true,
                "documentationFormat": ["markdown", "plaintext"],
                "deprecatedSupport": true,
                "tagSupport": { "valueSet": [1] },
                "insertReplaceSupport": true,
                "labelDetailsSupport": true,
                "resolveSupport": {
                  "properties": ["documentation", "detail", "additionalTextEdits"]
                }
              },
              "completionItemKind": {
                "valueSet": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25]
              },
              "completionList": {
                "itemDefaults": ["commitCharacters", "editRange", "insertTextFormat", "data"]
              }
            },
            "hover": {
              "contentFormat": ["markdown", "plaintext"]
            },
            "signatureHelp": {
              "contextSupport": true,
              "signatureInformation": {
                "documentationFormat": ["markdown", "plaintext"],
                "activeParameterSupport": true,
                "parameterInformation": { "labelOffsetSupport": true }
              }
            },
            "definition": { "linkSupport": true },
            "declaration": { "linkSupport": true },
            "typeDefinition": { "linkSupport": true },
            "implementation": { "linkSupport": true },
            "references": {},
            "documentHighlight": {},
            "documentSymbol": {
              "hierarchicalDocumentSymbolSupport": true,
              "symbolKind": {
                "valueSet": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26]
              },
              "tagSupport": { "valueSet": [1] }
            },
            "foldingRange": {
              "lineFoldingOnly": true,
              "foldingRangeKind": { "valueSet": ["comment", "imports", "region"] }
            },
            "selectionRange": {},
            "documentLink": { "tooltipSupport": true },
            "rename": { "prepareSupport": true },
            "codeAction": {
              "codeActionLiteralSupport": {
                "codeActionKind": {
                  "valueSet": [
                    "",
                    "quickfix",
                    "refactor",
                    "refactor.extract",
                    "refactor.inline",
                    "refactor.rewrite",
                    "source",
                    "source.organizeImports",
                    "source.fixAll"
                  ]
                }
              },
              "isPreferredSupport": true,
              "disabledSupport": true,
              "dataSupport": true,
              "resolveSupport": { "properties": ["edit"] }
            },
            "formatting": {},
            "rangeFormatting": {},
            "semanticTokens": {
              "requests": { "full": true },
              "tokenTypes": [
                "namespace",
                "type",
                "class",
                "enum",
                "interface",
                "struct",
                "typeParameter",
                "parameter",
                "variable",
                "property",
                "enumMember",
                "event",
                "function",
                "method",
                "macro",
                "keyword",
                "modifier",
                "comment",
                "string",
                "number",
                "regexp",
                "operator",
                "decorator"
              ],
              "tokenModifiers": [
                "declaration",
                "definition",
                "readonly",
                "static",
                "deprecated",
                "abstract",
                "async",
                "modification",
                "documentation",
                "defaultLibrary"
              ],
              "formats": ["relative"],
              "overlappingTokenSupport": false,
              "multilineTokenSupport": false,
              "augmentsSyntaxTokens": true
            }
          }
        }
        """;

    /// <summary>
    /// Returns a new copy of the capabilities.
    /// </summary>
    public static JsonNode Create()
    {
        return JsonNode.Parse(CapabilitiesJson)!;
    }
}
