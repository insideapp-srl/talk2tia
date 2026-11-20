using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Siemens.Engineering;

namespace Talk2Tia.Core.Openness;

/// <summary>Converts Openness values into JSON-friendly shapes without walking the object graph.</summary>
internal static class ValueFormatter
{
    private const int MaxListItems = 200;

    public static object? Format(object? value, int depth = 0)
    {
        switch (value)
        {
            case null:
                return null;
            case string or bool or int or long or short or byte or uint or ulong or ushort or sbyte or double or float or decimal:
                return value;
            case Enum e:
                return e.ToString();
            case DateTime dt:
                return dt == DateTime.MinValue ? null : dt.ToString("o", CultureInfo.InvariantCulture);
            case TimeSpan ts:
                return ts.ToString("c", CultureInfo.InvariantCulture);
            case Version v:
                return v.ToString();
            case Guid g:
                return g.ToString();
            case FileSystemInfo fsi:
                return fsi.FullName;
            case CultureInfo ci:
                return ci.Name;
            case MultilingualText mt:
                return FormatMultilingual(mt);
            case IEngineeringObject eo:
                return Describe(eo);
            case Type t:
                return t.FullName;
            case IEnumerable enumerable when depth < 1:
                return enumerable.Cast<object?>().Take(MaxListItems).Select(item => Format(item, depth + 1)).ToList();
            default:
                return value.ToString();
        }
    }

    public static Dictionary<string, string> FormatMultilingual(MultilingualText text)
    {
        var result = new Dictionary<string, string>();
        foreach (var item in text.Items)
        {
            if (!string.IsNullOrEmpty(item.Text))
            {
                result[item.Language.Culture.Name] = item.Text;
            }
        }

        return result;
    }

    /// <summary>Sets the text for one culture (or the first available culture when none is given).</summary>
    public static void SetMultilingual(MultilingualText text, string value, string? culture)
    {
        var items = text.Items.ToList();
        var target = culture is null
            ? items.FirstOrDefault()
            : items.FirstOrDefault(i => string.Equals(i.Language.Culture.Name, culture, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            throw new ArgumentException(
                $"Culture '{culture}' is not an active project language. Available: {string.Join(", ", items.Select(i => i.Language.Culture.Name))}");
        }

        target.Text = value;
    }

    public static string Describe(IEngineeringObject obj)
    {
        var name = TryGetName(obj);
        return name is null ? obj.GetType().Name : $"{obj.GetType().Name}:{name}";
    }

    public static string? TryGetName(IEngineeringObject obj)
    {
        try
        {
            return obj.GetAttribute("Name") as string;
        }
        catch (EngineeringException)
        {
            return null;
        }
    }

    /// <summary>Reads every readable attribute of an object into a dictionary.</summary>
    public static Dictionary<string, object?> ReadAttributes(IEngineeringObject obj, IEnumerable<string>? only = null)
    {
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        var filter = only?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var info in obj.GetAttributeInfos())
        {
            if (filter is not null && !filter.Contains(info.Name))
            {
                continue;
            }

            if (info.AccessMode == EngineeringAttributeAccessMode.Write)
            {
                continue;
            }

            try
            {
                result[info.Name] = Format(obj.GetAttribute(info.Name));
            }
            catch (EngineeringException ex)
            {
                result[info.Name] = $"<error: {ex.Message}>";
            }
        }

        return new Dictionary<string, object?>(result);
    }

    /// <summary>
    /// Converts a JSON-provided value (string/number/bool) to the attribute's expected CLR type.
    /// </summary>
    public static object? ConvertForAttribute(object? raw, IEnumerable<Type> supportedTypes)
    {
        var types = supportedTypes.ToList();
        if (raw is null)
        {
            return null;
        }

        if (raw is System.Text.Json.JsonElement je)
        {
            raw = je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => je.GetString(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Number => je.GetRawText(),
                System.Text.Json.JsonValueKind.Null => null,
                _ => je.GetRawText(),
            };
            if (raw is null)
            {
                return null;
            }
        }

        foreach (var type in types)
        {
            if (type.IsInstanceOfType(raw))
            {
                return raw;
            }
        }

        var text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
        foreach (var type in types)
        {
            var target = Nullable.GetUnderlyingType(type) ?? type;
            try
            {
                if (target.IsEnum)
                {
                    return Enum.Parse(target, text, ignoreCase: true);
                }

                if (target == typeof(Version))
                {
                    return Version.Parse(text);
                }

                if (target == typeof(TimeSpan))
                {
                    return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
                }

                if (target == typeof(Guid))
                {
                    return Guid.Parse(text);
                }

                if (typeof(IConvertible).IsAssignableFrom(target))
                {
                    return Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidCastException or OverflowException)
            {
                // Try the next supported type; the final error below reports all candidates.
                continue;
            }
        }

        throw new ArgumentException(
            $"Cannot convert '{text}' to any supported type: {string.Join(", ", types.Select(t => t.Name))}");
    }
}
