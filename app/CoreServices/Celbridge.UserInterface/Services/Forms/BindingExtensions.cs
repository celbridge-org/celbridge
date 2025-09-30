using System.Text.Json;

namespace Celbridge.UserInterface.Services.Forms;

public static class BindingExtensions
{
    public static bool IsBindingConfig(this JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = config.GetString();
        if (!string.IsNullOrEmpty(value) &&
            value.Length > 1 &&
            value.StartsWith('/'))
        {
            return true;
        }

        return false;
    }
}
