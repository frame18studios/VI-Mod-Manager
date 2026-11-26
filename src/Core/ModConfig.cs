using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViModManager.Models;

namespace ViModManager.Core;

/// <summary>Parses the variables a package exposes and converts user values safely.</summary>
public static class ModConfig
{
    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["boolean"] = "bool",
        ["checkbox"] = "bool",
        ["switch"] = "bool",
        ["integer"] = "int",
        ["number"] = "float",
        ["decimal"] = "float",
        ["slider"] = "float",
        ["select"] = "choice",
        ["enum"] = "choice",
        ["string"] = "text",
    };

    public static List<ConfigField> ParseSchema(JsonNode? raw)
    {
        var fields = new List<ConfigField>();
        if (raw is not JsonArray array)
        {
            return fields;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in array.OfType<JsonObject>())
        {
            string key = (ReadString(node["key"]) ?? ReadString(node["name"]) ?? "").Trim();
            if (key.Length == 0 || key.Any(char.IsControl) || !seen.Add(key))
            {
                continue;
            }

            string type = NormalizeType(ReadString(node["type"]));
            var options = new List<string>();
            if (node["options"] is JsonArray optionArray)
            {
                foreach (var option in optionArray)
                {
                    string text = ReadString(option) ?? "";
                    if (text.Length > 0)
                    {
                        options.Add(text);
                    }
                }
            }

            if (type == "choice" && options.Count == 0)
            {
                type = "text";
            }

            var field = new ConfigField
            {
                Key = key,
                Label = (ReadString(node["label"]) ?? Titleise(key)).Trim(),
                Type = type,
                Description = ReadString(node["description"]) ?? "",
                Options = options,
                Min = ReadDouble(node["min"]),
                Max = ReadDouble(node["max"]),
                Step = ReadDouble(node["step"]),
            };
            field.DefaultValue = DefaultFor(field);
            if (node["default"] is JsonNode defaultNode)
            {
                field.DefaultValue = FromJson(field, defaultNode);
            }

            fields.Add(field);
        }

        return fields;
    }

    public static string DefaultFor(ConfigField field) => field.Type switch
    {
        "bool" => "false",
        "int" => "0",
        "float" => "0",
        "choice" => field.Options.FirstOrDefault() ?? "",
        _ => "",
    };

    public static string Coerce(ConfigField field, string? value)
    {
        string text = value ?? "";
        switch (field.Type)
        {
            case "bool":
                return IsTruthy(text) ? "true" : "false";
            case "int":
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    number = Clamp(field, number);
                    return Math.Round(number).ToString(CultureInfo.InvariantCulture);
                }

                return field.DefaultValue;
            case "float":
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
                {
                    real = Clamp(field, real);
                    return real.ToString("0.######", CultureInfo.InvariantCulture);
                }

                return field.DefaultValue;
            case "choice":
                return field.Options.Contains(text) ? text : field.Options.FirstOrDefault() ?? "";
            default:
                return text;
        }
    }

    public static Dictionary<string, string> Resolve(IReadOnlyList<ConfigField> schema, IReadOnlyDictionary<string, string>? saved)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in schema)
        {
            string raw = saved is not null && saved.TryGetValue(field.Key, out string? stored)
                ? stored
                : field.DefaultValue;
            values[field.Key] = Coerce(field, raw);
        }

        return values;
    }

    /// <summary>Builds the JSON document written into the target folder for a mod.</summary>
    public static JsonObject BuildValues(IReadOnlyList<ConfigField> schema, IReadOnlyDictionary<string, string> config)
    {
        var result = new JsonObject();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in schema)
        {
            declared.Add(field.Key);
            string text = Coerce(field, config.TryGetValue(field.Key, out string? stored) ? stored : field.DefaultValue);
            result[field.Key] = field.Type switch
            {
                "bool" => JsonValue.Create(text == "true"),
                "int" => JsonValue.Create(int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer) ? integer : 0),
                "float" => JsonValue.Create(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real) ? real : 0d),
                _ => JsonValue.Create(text),
            };
        }

        foreach (var pair in config)
        {
            if (!declared.Contains(pair.Key))
            {
                result[pair.Key] = JsonValue.Create(pair.Value ?? "");
            }
        }

        return result;
    }

    public static string? ConfigDestination(JsonObject? manifest)
    {
        if (manifest is null)
        {
            return null;
        }

        string? explicitPath = ReadString(manifest["config_file"]);
        if (manifest["config"] is JsonObject config)
        {
            explicitPath ??= ReadString(config["file"]) ?? ReadString(config["destination"]);
        }
        else if (manifest["config"] is JsonValue configValue)
        {
            explicitPath ??= ReadString(configValue);
        }

        string text = (explicitPath ?? "").Trim();
        return text.Length == 0 ? null : text;
    }

    private static string NormalizeType(string? raw)
    {
        string type = (raw ?? "text").Trim().ToLowerInvariant();
        if (TypeAliases.TryGetValue(type, out string? alias))
        {
            type = alias;
        }

        return type is "bool" or "int" or "float" or "choice" or "text" ? type : "text";
    }

    private static string Titleise(string key) =>
        string.Join(' ', key.Replace('_', ' ').Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private static bool IsTruthy(string value) =>
        value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on" or "enabled";

    private static double Clamp(ConfigField field, double value)
    {
        if (field.Min is double min && value < min)
        {
            value = min;
        }

        if (field.Max is double max && value > max)
        {
            value = max;
        }

        return value;
    }

    private static string? ReadString(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.ToJsonString(),
            _ => null,
        },
        _ => null,
    };

    private static double? ReadDouble(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.GetValueKind() == JsonValueKind.Number && double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            return number;
        }

        return null;
    }

    private static string FromJson(ConfigField field, JsonNode node)
    {
        if (field.Type is "int" or "float" && node is JsonValue numeric && numeric.GetValueKind() == JsonValueKind.Number)
        {
            return Coerce(field, numeric.ToJsonString());
        }

        if (field.Type == "bool" && node is JsonValue boolean)
        {
            return boolean.GetValueKind() switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.String => Coerce(field, boolean.GetValue<string>()),
                JsonValueKind.Number => Coerce(field, boolean.ToJsonString()),
                _ => field.DefaultValue,
            };
        }

        return Coerce(field, ReadString(node) ?? field.DefaultValue);
    }
}
