#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

[InitializeOnLoad]
internal static class VLiveKitInstallerBootstrap
{
    private const string PackageId = "com.toshi.vlivekit";
    private const string RegistryName = "toshi";
    private const string RegistryUrl = "https://registry.npmjs.org/";
    private const string RegistryScope = "com.toshi";
    private const string PromptPrefsKeyPrefix = "VLiveKitInstallerBootstrap.Prompt.";

    private static AddRequest addRequest;

    static VLiveKitInstallerBootstrap()
    {
        EditorApplication.delayCall += ShowPromptOnce;
    }

    private static void ShowPromptOnce()
    {
        if (IsVLiveKitInstallerInstalled())
        {
            return;
        }

        var key = PromptPrefsKeyPrefix + Application.dataPath;
        if (EditorPrefs.GetBool(key, false))
        {
            return;
        }

        var backupNotice = ProjectHasGitMetadata()
            ? ""
            : "\n\nThis Unity project does not appear to be managed with Git. Make a project backup before installing packages.";
        var result = EditorUtility.DisplayDialogComplex(
            "VLiveKitInstaller",
            "Install VLiveKitInstaller with Unity Package Manager?" + backupNotice,
            "Install",
            "Later",
            "Do Not Show Again");

        if (result == 0)
        {
            Install();
        }
        else if (result == 2)
        {
            EditorPrefs.SetBool(key, true);
        }
    }

    private static void Install()
    {
        if (IsVLiveKitInstallerInstalled())
        {
            OpenInstallerIfAvailable();
            return;
        }

        EnsureScopedRegistry();
        AssetDatabase.Refresh();
        addRequest = Client.Add(PackageId);
        EditorApplication.update += UpdateInstallRequest;
    }

    private static void UpdateInstallRequest()
    {
        if (addRequest == null || !addRequest.IsCompleted)
        {
            return;
        }

        EditorApplication.update -= UpdateInstallRequest;
        if (addRequest.Status == StatusCode.Success)
        {
            Debug.Log("VLiveKitInstaller installed.");
            EditorApplication.delayCall += OpenInstallerIfAvailable;
        }
        else
        {
            var message = addRequest.Error != null ? addRequest.Error.message : "Unknown Package Manager error.";
            EditorUtility.DisplayDialog("VLiveKitInstaller", "Failed to install VLiveKitInstaller.\n\n" + message, "OK");
            Debug.LogError("Failed to install VLiveKitInstaller: " + message);
        }

        addRequest = null;
    }

    private static bool IsVLiveKitInstallerInstalled()
    {
        var manifestJson = ReadManifestJson();
        if (!string.IsNullOrEmpty(manifestJson) && manifestJson.Contains("\"" + PackageId + "\""))
        {
            return true;
        }

        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(PackageId);
        return packageInfo != null;
    }

    private static void OpenInstallerIfAvailable()
    {
        EditorApplication.ExecuteMenuItem("toshi/VLiveKit Installer");
    }

    private static void EnsureScopedRegistry()
    {
        var manifestPath = ProjectPath("Packages/manifest.json");
        if (!File.Exists(manifestPath))
        {
            return;
        }

        var json = File.ReadAllText(manifestPath);
        if (RegistryWithScopeExists(json))
        {
            return;
        }

        var registryBlock =
            "    {\n" +
            "      \"name\": \"" + RegistryName + "\",\n" +
            "      \"url\": \"" + RegistryUrl + "\",\n" +
            "      \"scopes\": [\n" +
            "        \"" + RegistryScope + "\"\n" +
            "      ]\n" +
            "    }";

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
                return;
            }

            updatedJson = json.Insert(
                braceIndex + 1,
                "\n  \"scopedRegistries\": [\n" +
                registryBlock +
                "\n  ],");
        }

        File.WriteAllText(manifestPath, updatedJson, new UTF8Encoding(false));
    }

    private static bool RegistryWithScopeExists(string json)
    {
        return Regex.IsMatch(json, "\"url\"\\s*:\\s*\"https://registry\\.npmjs\\.org/?\"[\\s\\S]*\"scopes\"\\s*:\\s*\\[[\\s\\S]*\"" + Regex.Escape(RegistryScope) + "\"[\\s\\S]*\\]");
    }

    private static bool ProjectHasGitMetadata()
    {
        var gitPath = ProjectPath(".git");
        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }

    private static string ReadManifestJson()
    {
        var manifestPath = ProjectPath("Packages/manifest.json");
        return File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : "";
    }

    private static string ProjectPath(string projectRelativePath)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRelativePath));
    }
}
#endif
