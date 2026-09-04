namespace Cowork.Core.Configuration;

public enum ConfigFormat
{
    /// <summary>Đoán theo phần mở rộng của file.</summary>
    Auto = 0,
    Json = 1,
    Ini = 2,
    Xml = 3,
    PlainText = 4,
}

public static class ConfigFormatExtensions
{
    /// <summary>Suy ra định dạng thật khi người dùng để <see cref="ConfigFormat.Auto"/>.</summary>
    public static ConfigFormat Resolve(this ConfigFormat format, string path)
    {
        if (format != ConfigFormat.Auto)
            return format;

        var ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
        return ext switch
        {
            ".json" or ".jsonc" => ConfigFormat.Json,
            ".ini" or ".cfg" or ".conf" or ".properties" or ".env" => ConfigFormat.Ini,
            ".xml" or ".config" or ".xaml" or ".csproj" => ConfigFormat.Xml,
            _ => ConfigFormat.PlainText,
        };
    }

    public static string ToLabel(this ConfigFormat format) => format switch
    {
        ConfigFormat.Auto => "Tự nhận",
        ConfigFormat.Json => "JSON",
        ConfigFormat.Ini => "INI / .env",
        ConfigFormat.Xml => "XML",
        _ => "Văn bản",
    };
}
