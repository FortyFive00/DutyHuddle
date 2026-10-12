using System.Text;
using System.Text.Json;

namespace DutyHuddle.Configuration;

/// <summary>Export and import of the configuration as a single base64 string for the clipboard.</summary>
public static class ConfigTransfer
{
    private const string Prefix = "DHCFG1:";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Export(PluginConfig config)
    {
        var json = JsonSerializer.Serialize(config, Options);
        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryImport(string text, out PluginConfig? config, out string error)
    {
        config = null;
        error = string.Empty;
        text = text.Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = "This is not a DutyHuddle configuration string.";
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(text[Prefix.Length..]));
            config = JsonSerializer.Deserialize<PluginConfig>(json, Options);
            if (config is null || config.Version > PluginConfig.CurrentVersion)
            {
                error = "The configuration comes from a newer version of DutyHuddle.";
                config = null;
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or DecoderFallbackException)
        {
            error = "The configuration string is damaged.";
            return false;
        }
    }
}
