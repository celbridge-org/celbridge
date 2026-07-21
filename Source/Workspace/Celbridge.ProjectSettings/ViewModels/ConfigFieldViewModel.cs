using System.Globalization;
using System.Text.Json;
using Celbridge.Packages;
using Celbridge.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// One editable field in a contribution's descriptor form, backed by a single config descriptor. The
/// field exposes typed bindable state plus per-type visibility flags so the form can show the right
/// control, and commits every change to the owning panel as a typed config edit, dropping the key
/// when the value returns to the descriptor default so the file stays minimal.
/// </summary>
public partial class ConfigFieldViewModel : ObservableObject
{
    private readonly ConfigDescriptor _descriptor;
    private readonly string _packageName;
    private readonly string _contributionId;
    private readonly Action<string, string, string, ConfigEditValue?> _commit;

    // Commits are suppressed until initial values are set, so building the field from disk does not
    // write it back.
    private bool _initialized;

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private string _stringValue = string.Empty;

    [ObservableProperty]
    private double _numberValue;

    [ObservableProperty]
    private string _stringListText = string.Empty;

    public ConfigFieldViewModel(
        ConfigDescriptor descriptor,
        string packageName,
        string contributionId,
        object? rawValue,
        string displayName,
        Action<string, string, string, ConfigEditValue?> commit)
    {
        _descriptor = descriptor;
        _packageName = packageName;
        _contributionId = contributionId;
        _commit = commit;

        DisplayName = displayName;

        InitializeValue(rawValue);

        _initialized = true;
    }

    public string Key => _descriptor.Key;
    public string DisplayName { get; }
    public IReadOnlyList<string> EnumValues => _descriptor.Values;
    public string StringListPlaceholder => ProjectSettingsLabels.StringListPlaceholder;

    public bool IsBool => _descriptor.Type == ConfigValueType.Bool;
    public bool IsString => _descriptor.Type == ConfigValueType.String;
    public bool IsNumber => _descriptor.Type == ConfigValueType.Number;
    public bool IsEnum => _descriptor.Type == ConfigValueType.Enum;
    public bool IsStringList => _descriptor.Type == ConfigValueType.StringList;

    private void InitializeValue(object? rawValue)
    {
        switch (_descriptor.Type)
        {
            case ConfigValueType.Bool:
                BoolValue = rawValue is bool boolValue
                    ? boolValue
                    : string.Equals(_descriptor.DefaultValue, "true", StringComparison.Ordinal);
                break;

            case ConfigValueType.String:
            case ConfigValueType.Enum:
                StringValue = rawValue as string ?? _descriptor.DefaultValue ?? string.Empty;
                break;

            case ConfigValueType.Number:
                NumberValue = ReadNumber(rawValue);
                break;

            case ConfigValueType.StringList:
                StringListText = string.Join("\n", ReadStringList(rawValue));
                break;
        }
    }

    private double ReadNumber(object? rawValue)
    {
        switch (rawValue)
        {
            case long longValue:
                return longValue;
            case double doubleValue:
                return doubleValue;
        }

        if (double.TryParse(_descriptor.DefaultValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private IReadOnlyList<string> ReadStringList(object? rawValue)
    {
        if (rawValue is IReadOnlyList<string> list)
        {
            return list;
        }

        if (!string.IsNullOrEmpty(_descriptor.DefaultValue))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(_descriptor.DefaultValue);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // A malformed default degrades to an empty list.
            }
        }

        return Array.Empty<string>();
    }

    partial void OnBoolValueChanged(bool value) => Commit();

    partial void OnStringValueChanged(string value) => Commit();

    partial void OnNumberValueChanged(double value) => Commit();

    partial void OnStringListTextChanged(string value) => Commit();

    // Sends the current value to the panel, or a remove when it matches the descriptor default so the
    // file only records overrides.
    private void Commit()
    {
        if (!_initialized)
        {
            return;
        }

        var value = BuildEditValue();
        if (NormalizeValue(value) == _descriptor.DefaultValue)
        {
            _commit(_packageName, _contributionId, Key, null);
        }
        else
        {
            _commit(_packageName, _contributionId, Key, value);
        }
    }

    private ConfigEditValue BuildEditValue()
    {
        switch (_descriptor.Type)
        {
            case ConfigValueType.Bool:
                return new BoolEditValue(BoolValue);

            case ConfigValueType.Number:
            {
                var number = NumberValue;
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    // An empty or malformed NumberBox yields NaN; fall back to the descriptor default
                    // rather than committing "NaN" into the config.
                    number = ReadNumber(null);
                }
                if (number == Math.Truncate(number))
                {
                    return new IntegerEditValue((long)number);
                }

                return new FloatEditValue(number);
            }

            case ConfigValueType.StringList:
                return new StringListEditValue(ParseStringList(StringListText));

            default:
                return new StringEditValue(StringValue);
        }
    }

    private static IReadOnlyList<string> ParseStringList(string text)
    {
        return text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    // Renders an edit value in the same normalized string form used by the descriptor default, so a
    // value equal to the default can be detected and the key dropped.
    private static string NormalizeValue(ConfigEditValue value)
    {
        switch (value)
        {
            case BoolEditValue boolValue:
                return boolValue.Value ? "true" : "false";

            case StringEditValue stringValue:
                return stringValue.Value;

            case IntegerEditValue integerValue:
                return integerValue.Value.ToString(CultureInfo.InvariantCulture);

            case FloatEditValue floatValue:
                return floatValue.Value.ToString(CultureInfo.InvariantCulture);

            case StringListEditValue stringListValue:
                return JsonSerializer.Serialize(stringListValue.Values);

            default:
                return string.Empty;
        }
    }
}
