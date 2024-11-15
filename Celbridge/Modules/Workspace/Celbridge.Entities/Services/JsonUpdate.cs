using System.Text.Json.Nodes;

namespace Celbridge.Entities;

public class JsonUpdate
{
    private readonly List<Func<JsonObject, Result>> _operations = new();
    private readonly List<Func<JsonObject, Result>> _undoOperations = new();

    public void Set(string path, JsonNode value)
    {
        _operations.Add(json =>
        {
            var token = json.SelectToken(path);
            var parentPath = path.ParentPath();
            var parent = json.SelectToken(parentPath) as JsonObject;

            if (parent == null)
            {
                parent = json;
            }

            if (token != null)
            {
                var originalValue = token.DeepClone();
                token.ReplaceWith(value);
                _undoOperations.Insert(0, json =>
                {
                    json.SelectToken(path)?.ReplaceWith(originalValue);
                    return Result.Ok();
                });
            }
            else
            {
                parent[path.LastSegment()] = value;
                _undoOperations.Insert(0, json =>
                {
                    parent.Remove(path.LastSegment());
                    return Result.Ok();
                });
            }

            return Result.Ok();
        });
    }

    public void Remove(string path)
    {
        _operations.Add(json =>
        {
            var token = json.SelectToken(path);
            if (token == null)
            {
                return Result.Fail($"Remove operation failed: Path '{path}' not found.");
            }

            var originalValue = token.DeepClone();

            if (token.Parent is JsonObject parent)
            {
                parent.Remove(token.GetPropertyName());
            }

            _undoOperations.Insert(0, json =>
            {
                var parentToken = json.SelectToken(path.ParentPath()) as JsonObject;
                if (parentToken is null)
                {
                    json[path.LastSegment()] = originalValue;
                }
                else
                {
                    parentToken[path.LastSegment()] = originalValue;
                }

                return Result.Ok();
            });

            return Result.Ok();
        });
    }

    public void Move(string fromPath, string toPath)
    {
        _operations.Add(json =>
        {
            var fromToken = json.SelectToken(fromPath);
            if (fromToken == null)
            {
                return Result.Fail($"Move operation failed: Path '{fromPath}' not found.");
            }

            var toParent = json.SelectToken(toPath.ParentPath()) as JsonObject;
            if (toParent == null)
            {
                toParent = json;
            }

            var movedValue = fromToken.DeepClone();

            if (fromToken.Parent is JsonObject fromParent)
            {
                fromParent.Remove(fromPath.LastSegment());
            }

            toParent[toPath.LastSegment()] = movedValue;

            _undoOperations.Insert(0, json =>
            {
                toParent.Remove(toPath.LastSegment());

                var parentToken = json.SelectToken(fromPath.ParentPath()) as JsonObject;
                if (parentToken != null)
                {
                    parentToken[fromPath.LastSegment()] = movedValue;
                }
                return Result.Ok();
            });

            return Result.Ok();
        });
    }

    public void Copy(string fromPath, string toPath)
    {
        _operations.Add(json =>
        {
            var fromToken = json.SelectToken(fromPath);
            if (fromToken == null)
                return Result.Fail($"Copy operation failed: Path '{fromPath}' not found.");

            var toParent = json.SelectToken(toPath.ParentPath()) as JsonObject;
            if (toParent == null)
            {
                toParent = json;
            }

            toParent[toPath.LastSegment()] = fromToken.DeepClone();
            _undoOperations.Insert(0, json =>
            {
                toParent.Remove(toPath.LastSegment());
                return Result.Ok();
            });

            return Result.Ok();
        });
    }

    public void Test(string path, JsonNode expectedValue)
    {
        _operations.Add(json =>
        {
            var token = json.SelectToken(path);
            if (token == null || !JsonNode.DeepEquals(token, expectedValue))
            {
                return Result.Fail($"Test operation failed: Value at '{path}' does not match expected value.");
            }

            return Result.Ok();
        });
    }

    public Result Apply(JsonObject json)
    {
        foreach (var operation in _operations)
        {
            var result = operation(json);
            if (result.IsFailure)
            {
                return result;
            }
        }
        return Result.Ok();
    }

    public Result Undo(JsonObject json)
    {
        foreach (var undoOperation in _undoOperations)
        {
            var result = undoOperation(json);
            if (result.IsFailure)
            {
                return result;
            }
        }
        return Result.Ok();
    }
}

public static class JsonPathExtensions
{
    public static string ParentPath(this string path) => path.Contains('.') ? path.Substring(0, path.LastIndexOf('.')) : string.Empty;
    public static string LastSegment(this string path) => path.Contains('.') ? path.Substring(path.LastIndexOf('.') + 1) : path;

    public static JsonNode? SelectToken(this JsonObject json, string path)
    {
        var segments = path.Split('.');
        JsonNode? current = json;

        foreach (var segment in segments)
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out var value))
            {
                current = value;
            }
            else
            {
                return null;
            }
        }

        return current;
    }
}
