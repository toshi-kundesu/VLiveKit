#if UNITY_EDITOR
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

internal static class VLiveKitManifestUtility
{
    private const string ToshiRegistryName = "toshi";
    private const string NpmRegistryUrl = "https://registry.npmjs.org/";
    private const string ToshiScope = "com.toshi";

    internal static bool EnsureVLiveKitScopedRegistry()
    {
        return EnsureScopedRegistry(ToshiRegistryName, NpmRegistryUrl, ToshiScope);
    }

    internal static bool EnsureExternalScopedRegistry()
    {
        return EnsureScopedRegistry("npmjs", "https://registry.npmjs.com", "jp.keijiro", "com.hecomi");
    }

    private static bool EnsureScopedRegistry(string registryName, string registryUrl, params string[] scopes)
    {
        var manifestPath = ProjectPath("Packages/manifest.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        var json = File.ReadAllText(manifestPath);
        if (TryFindRegistryWithUrl(json, registryUrl, out var existingRegistry) ||
            TryFindRegistryWithUrl(json, registryUrl.TrimEnd('/'), out existingRegistry))
        {
            var updatedRegistry = existingRegistry.Text;
            var changed = false;
            foreach (var scope in scopes)
            {
                if (RegistryContainsScope(updatedRegistry, scope))
                {
                    continue;
                }

                updatedRegistry = AddScopeToRegistry(updatedRegistry, scope);
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            WriteManifest(manifestPath, json.Substring(0, existingRegistry.StartIndex) + updatedRegistry + json.Substring(existingRegistry.StartIndex + existingRegistry.Length));
            return true;
        }

        var registryBlock = BuildRegistryBlock(registryName, registryUrl, scopes, "    ");
        string updatedJson;
        var scopedRegistryMatch = Regex.Match(json, "\"scopedRegistries\"\\s*:\\s*\\[");
        if (scopedRegistryMatch.Success)
        {
            var insertIndex = scopedRegistryMatch.Index + scopedRegistryMatch.Length;
            var nextIndex = insertIndex;
            while (nextIndex < json.Length && char.IsWhiteSpace(json[nextIndex]))
            {
                nextIndex++;
            }

            var suffix = nextIndex < json.Length && json[nextIndex] == ']'
                ? "\n" + registryBlock + "\n  "
                : "\n" + registryBlock + ",";
            updatedJson = json.Insert(insertIndex, suffix);
        }
        else
        {
            var braceIndex = json.IndexOf('{');
            if (braceIndex < 0)
            {
                return false;
            }

            var scopedRegistriesBlock =
                "\n  \"scopedRegistries\": [\n" +
                registryBlock +
                "\n  ],";
            updatedJson = json.Insert(braceIndex + 1, scopedRegistriesBlock);
        }

        WriteManifest(manifestPath, updatedJson);
        return true;
    }

    private static bool TryFindRegistryWithUrl(string json, string registryUrl, out JsonRange registry)
    {
        var urlMatch = Regex.Match(json, "\"url\"\\s*:\\s*\"" + Regex.Escape(registryUrl) + "\"");
        while (urlMatch.Success)
        {
            var start = json.LastIndexOf('{', urlMatch.Index);
            if (start >= 0 && TryFindObjectEnd(json, start, out var end))
            {
                registry = new JsonRange(start, end - start + 1, json.Substring(start, end - start + 1));
                return true;
            }

            urlMatch = urlMatch.NextMatch();
        }

        registry = new JsonRange(0, 0, null);
        return false;
    }

    private static bool TryFindObjectEnd(string json, int start, out int end)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < json.Length; i++)
        {
            var character = json[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                depth--;
                if (depth == 0)
                {
                    end = i;
                    return true;
                }
            }
        }

        end = -1;
        return false;
    }

    private static bool RegistryContainsScope(string registryBlock, string scope)
    {
        return Regex.IsMatch(registryBlock, "\"scopes\"\\s*:\\s*\\[[\\s\\S]*\"" + Regex.Escape(scope) + "\"[\\s\\S]*\\]");
    }

    private static string AddScopeToRegistry(string registryBlock, string scope)
    {
        var scopesMatch = Regex.Match(registryBlock, "\"scopes\"\\s*:\\s*\\[");
        if (!scopesMatch.Success)
        {
            var insertIndex = registryBlock.LastIndexOf('}');
            if (insertIndex < 0)
            {
                return registryBlock;
            }

            return registryBlock.Insert(insertIndex, "  \"scopes\": [\n        \"" + scope + "\"\n      ]\n");
        }

        var insertAt = scopesMatch.Index + scopesMatch.Length;
        var nextIndex = insertAt;
        while (nextIndex < registryBlock.Length && char.IsWhiteSpace(registryBlock[nextIndex]))
        {
            nextIndex++;
        }

        var suffix = nextIndex < registryBlock.Length && registryBlock[nextIndex] == ']'
            ? "\n        \"" + scope + "\"\n      "
            : "\n        \"" + scope + "\",";
        return registryBlock.Insert(insertAt, suffix);
    }

    private static string BuildRegistryBlock(string registryName, string registryUrl, string[] scopes, string indent)
    {
        var builder = new StringBuilder();
        builder.AppendLine(indent + "{");
        builder.AppendLine(indent + "  \"name\": \"" + registryName + "\",");
        builder.AppendLine(indent + "  \"url\": \"" + registryUrl + "\",");
        builder.AppendLine(indent + "  \"scopes\": [");
        for (var i = 0; i < scopes.Length; i++)
        {
            builder.Append(indent + "    \"" + scopes[i] + "\"");
            builder.AppendLine(i + 1 < scopes.Length ? "," : "");
        }

        builder.Append(indent + "  ]");
        builder.AppendLine();
        builder.Append(indent + "}");
        return builder.ToString();
    }

    private static void WriteManifest(string manifestPath, string json)
    {
        File.WriteAllText(manifestPath, json, new UTF8Encoding(false));
        AssetDatabase.Refresh();
    }

    private static string ProjectPath(string unityRelativePath)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", unityRelativePath));
    }

    private readonly struct JsonRange
    {
        public JsonRange(int startIndex, int length, string text)
        {
            StartIndex = startIndex;
            Length = length;
            Text = text;
        }

        public int StartIndex { get; }
        public int Length { get; }
        public string Text { get; }
    }
}
#endif
