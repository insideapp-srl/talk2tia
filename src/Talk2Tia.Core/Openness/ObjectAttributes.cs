using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ModelContextProtocol;
using Siemens.Engineering;

namespace Talk2Tia.Core.Openness;

internal static class ObjectAttributes
{
    /// <summary>
    /// Sets attributes on an Openness object, converting JSON values to the attribute's supported type.
    /// Multilingual texts (e.g. Title/Comment) are set for one culture.
    /// </summary>
    public static List<string> Apply(IEngineeringObject obj, IDictionary<string, JsonElement> attributes, string? culture,
        IDictionary<string, MultilingualText>? multilingual = null)
    {
        var infos = obj.GetAttributeInfos().ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        var applied = new List<string>();
        foreach (var pair in attributes)
        {
            if (multilingual is not null && multilingual.TryGetValue(pair.Key, out var text))
            {
                ValueFormatter.SetMultilingual(text, pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString() ?? string.Empty : pair.Value.GetRawText(), culture);
                applied.Add(pair.Key);
                continue;
            }

            if (!infos.TryGetValue(pair.Key, out var info))
            {
                throw new McpException(
                    $"Attribute '{pair.Key}' does not exist on {obj.GetType().Name}. Writable attributes: " +
                    string.Join(", ", infos.Values.Where(i => i.AccessMode != EngineeringAttributeAccessMode.Read).Select(i => i.Name)));
            }

            if (info.AccessMode == EngineeringAttributeAccessMode.Read)
            {
                throw new McpException($"Attribute '{info.Name}' is read-only.");
            }

            var current = SafeGet(obj, info.Name);
            if (current is MultilingualText mlt)
            {
                ValueFormatter.SetMultilingual(mlt, pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString() ?? string.Empty : pair.Value.GetRawText(), culture);
            }
            else
            {
                obj.SetAttribute(info.Name, ValueFormatter.ConvertForAttribute(pair.Value, info.SupportedTypes));
            }

            applied.Add(info.Name);
        }

        return applied;
    }

    private static object? SafeGet(IEngineeringObject obj, string name)
    {
        try
        {
            return obj.GetAttribute(name);
        }
        catch (EngineeringException)
        {
            // Write-only attributes cannot be read; treat as a plain value.
            return null;
        }
    }
}
