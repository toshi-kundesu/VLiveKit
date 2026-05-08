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

        BootstrapPromptWindow.Open(key, !ProjectHasGitMetadata(), Install);
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
            Debug.Log("VLiveKitPackageManager installed.");
            EditorApplication.delayCall += OpenInstallerIfAvailable;
        }
        else
        {
            var message = addRequest.Error != null ? addRequest.Error.message : "Unknown Package Manager error.";
            Debug.Log("VLiveKitPackageManager could not install yet. " + message);
            BootstrapPromptWindow.Open(PromptPrefsKeyPrefix + Application.dataPath, !ProjectHasGitMetadata(), Install);
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
        EditorApplication.ExecuteMenuItem("toshi/VLiveKit Package Manager");
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

    private sealed class BootstrapPromptWindow : EditorWindow
    {
        private string prefsKey;
        private bool showBackupTip;
        private Action installAction;

        public static void Open(string prefsKey, bool showBackupTip, Action installAction)
        {
            var window = GetWindow<BootstrapPromptWindow>(true, "VLiveKitPackageManager");
            window.prefsKey = prefsKey;
            window.showBackupTip = showBackupTip;
            window.installAction = installAction;
            window.minSize = new Vector2(430f, 190f);
            window.maxSize = new Vector2(430f, 190f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            GUILayout.Space(12f);
            GUILayout.Label("Set up VLiveKitPackageManager", EditorStyles.boldLabel);
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("This adds the com.toshi scoped registry and installs only the installer package.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("After that, you can choose packages and samples from one small window.", EditorStyles.wordWrappedLabel);

            if (showBackupTip)
            {
                GUILayout.Space(8f);
                var rect = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.185f, 0.185f, 0.185f) : new Color(0.935f, 0.935f, 0.922f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.09f) : new Color(0f, 0f, 0f, 0.11f));
                GUI.Label(
                    rect,
                    "Good to go. Keeping a project backup is a nice safety net if this project is not tracked with Git.",
                    new GUIStyle(EditorStyles.wordWrappedLabel)
                    {
                        padding = new RectOffset(10, 10, 7, 7),
                        normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.92f, 0.92f, 0.90f) : new Color(0.12f, 0.12f, 0.11f) }
                    });
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Set Up", GUILayout.Height(28f)))
            {
                EditorPrefs.SetBool(prefsKey, true);
                Close();
                installAction?.Invoke();
            }

            if (GUILayout.Button("Not Now", GUILayout.Height(28f)))
            {
                EditorPrefs.SetBool(prefsKey, true);
                Close();
            }

            if (GUILayout.Button("Don't Ask Again", GUILayout.Height(28f)))
            {
                EditorPrefs.SetBool(prefsKey, true);
                Close();
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }
    }
}
#endif
