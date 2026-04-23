using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace KeyVolumeBridge.Config;

internal sealed class AppConfig
{
    private const string ConfigFileName = "KeyVolumeBridge.config.json";
    internal static string ConfigPath => Path.Combine(AppContext.BaseDirectory, ConfigFileName);

    public OscConfig Osc { get; set; } = new();
    public CommandConfig Commands { get; set; } = new();
    public ClickConfig Click { get; set; } = new();

    public static AppConfig Load()
    {
        string path = ConfigPath;
        if (!File.Exists(path))
        {
            AppConfig defaultConfig = CreateDefault();
            TrySave(path, defaultConfig);
            return defaultConfig;
        }

        try
        {
            string json = File.ReadAllText(path);
            AppConfig? config = JsonSerializer.Deserialize<AppConfig>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (config == null)
            {
                Console.Error.WriteLine($"Конфиг '{path}' пустой или поврежден. Используются значения по умолчанию.");
                return CreateDefault();
            }

            config.Normalize();
            return config;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Не удалось загрузить конфиг '{path}': {ex.Message}. Используются значения по умолчанию.");
            return CreateDefault();
        }
    }

    private static void Save(string path, AppConfig config)
    {
        config.Normalize();
        string json = JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(path, json);
    }

    private static void TrySave(string path, AppConfig config)
    {
        try
        {
            Save(path, config);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Не удалось сохранить конфиг '{path}': {ex.Message}");
        }
    }

    private static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            Osc = new OscConfig
            {
                Host = "127.0.0.1",
                Port = 8000
            },
            Commands = new CommandConfig
            {
                VolumeUp = "40108",
                VolumeDown = "40107",
                Mute = new MuteCommandConfig
                {
                    SingleClick = "40730",
                    DoubleClickExtra = "",
                    TripleClickExtra = ""
                }
            },
            Click = new ClickConfig
            {
                MuteWindowMs = 325
            }
        };
    }

    private void Normalize()
    {
        Osc ??= new OscConfig();
        Commands ??= new CommandConfig();
        Commands.Mute ??= new MuteCommandConfig();
        Click ??= new ClickConfig();

        if (string.IsNullOrWhiteSpace(Osc.Host)) Osc.Host = "127.0.0.1";

        if (Osc.Port is <= 0 or > 65535) Osc.Port = 8000;

        if (Click.MuteWindowMs < 100) Click.MuteWindowMs = 325;

        if (Click.MuteWindowMs > 3000) Click.MuteWindowMs = 3000;

        Commands.VolumeUp = NormalizeCommandId(Commands.VolumeUp);
        Commands.VolumeDown = NormalizeCommandId(Commands.VolumeDown);
        Commands.Mute.SingleClick = NormalizeCommandId(Commands.Mute.SingleClick);
        Commands.Mute.DoubleClickExtra = NormalizeCommandId(Commands.Mute.DoubleClickExtra);
        Commands.Mute.TripleClickExtra = NormalizeCommandId(Commands.Mute.TripleClickExtra);
    }

    private static string NormalizeCommandId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string trimmed = value.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric))
            return numeric <= 0 ? string.Empty : numeric.ToString(CultureInfo.InvariantCulture);

        return trimmed;
    }
}

internal sealed class OscConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8000;
}

internal sealed class CommandConfig
{
    [JsonConverter(typeof(StringOrNumberToStringJsonConverter))]
    public string VolumeUp { get; set; } = "40108";
    [JsonConverter(typeof(StringOrNumberToStringJsonConverter))]
    public string VolumeDown { get; set; } = "40107";
    public MuteCommandConfig Mute { get; set; } = new();
}

internal sealed class MuteCommandConfig
{
    [JsonConverter(typeof(StringOrNumberToStringJsonConverter))]
    public string SingleClick { get; set; } = "40730";
    [JsonConverter(typeof(StringOrNumberToStringJsonConverter))]
    public string DoubleClickExtra { get; set; } = string.Empty;
    [JsonConverter(typeof(StringOrNumberToStringJsonConverter))]
    public string TripleClickExtra { get; set; } = string.Empty;
}

internal sealed class ClickConfig
{
    public int MuteWindowMs { get; set; } = 325;
}

internal sealed class StringOrNumberToStringJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => JsonElement.ParseValue(ref reader).ToString(),
            JsonTokenType.Null => string.Empty,
            _ => throw new JsonException("Ожидалась строка или число для command id.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
