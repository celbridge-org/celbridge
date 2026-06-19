using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Celbridge.Utilities;

/// <summary>
/// The in-memory core shared by every JSON-backed settings store: a string-keyed
/// dictionary of JSON-serialized values plus the per-key serialize and deserialize.
/// Each concrete store composes one of these and adds only its file IO and flush
/// policy. Holds no file path and performs no IO of its own.
/// </summary>
public sealed class KeyValueStore
{
    private static readonly JsonSerializerOptions FileSerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly Dictionary<string, string> _entries;

    public KeyValueStore()
        : this(new Dictionary<string, string>())
    {
    }

    private KeyValueStore(Dictionary<string, string> entries)
    {
        _entries = entries;
    }

    /// <summary>
    /// Returns true and the deserialized value when the key holds a value that
    /// reads back as T; false otherwise, leaving value at its default.
    /// </summary>
    public bool TryGetValue<T>(string key, out T value) where T : notnull
    {
        value = default!;

        if (!_entries.TryGetValue(key, out var json))
        {
            return false;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<T>(json);
            if (deserialized is null)
            {
                return false;
            }

            value = deserialized;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true and the raw stored JSON when the key is present, false
    /// otherwise. For callers that deserialize to a nullable target type, which
    /// the notnull-constrained TryGetValue cannot express.
    /// </summary>
    public bool TryGetSerialized(string key, [MaybeNullWhen(false)] out string json)
    {
        return _entries.TryGetValue(key, out json);
    }

    /// <summary>
    /// Serializes the value and stores it under the key, replacing any existing value.
    /// </summary>
    public void SetValue<T>(string key, T value) where T : notnull
    {
        _entries[key] = JsonSerializer.Serialize(value);
    }

    public bool ContainsKey(string key)
    {
        return _entries.ContainsKey(key);
    }

    /// <summary>
    /// Removes the stored value for the key, returning true when a value was removed.
    /// </summary>
    public bool Remove(string key)
    {
        return _entries.Remove(key);
    }

    /// <summary>
    /// Serializes the whole store to an indented JSON object suitable for writing
    /// to a settings file.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(_entries, FileSerializerOptions);
    }

    /// <summary>
    /// Builds a store from a settings file's contents, returning an empty store
    /// when the content is blank or cannot be parsed. A corrupt file is treated as
    /// empty rather than throwing, since the stored data is regenerable.
    /// </summary>
    public static KeyValueStore FromJson(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new KeyValueStore();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
            if (parsed is null)
            {
                return new KeyValueStore();
            }

            return new KeyValueStore(parsed);
        }
        catch (JsonException)
        {
            return new KeyValueStore();
        }
    }
}
