using System;
using System.IO;
using ModelContextProtocol;

namespace Talk2Tia.Core.Tools;

internal static class Guard
{
    /// <summary>Destructive operations must be confirmed explicitly by the caller.</summary>
    public static void RequireConfirm(bool confirm, string action)
    {
        if (!confirm)
        {
            throw new McpException(
                $"'{action}' changes or deletes data and needs explicit approval. Ask the user, then call again with confirm=true.");
        }
    }

    public static string RequireText(string? value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new McpException($"Parameter '{parameter}' is required.");
        }

        return value!;
    }

    public static FileInfo ExistingFile(string path)
    {
        var file = new FileInfo(Environment.ExpandEnvironmentVariables(path));
        if (!file.Exists)
        {
            throw new McpException($"File not found: {file.FullName}");
        }

        return file;
    }

    public static DirectoryInfo Directory(string path, bool create = false)
    {
        var dir = new DirectoryInfo(Environment.ExpandEnvironmentVariables(path));
        if (!dir.Exists)
        {
            if (!create)
            {
                throw new McpException($"Directory not found: {dir.FullName}");
            }

            dir.Create();
        }

        return dir;
    }

    public static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new McpException($"Invalid value '{value}'. Allowed: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}");
    }
}
