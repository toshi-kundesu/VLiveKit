#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

[InitializeOnLoad]
internal sealed class VLiveKitInstallerWindow : EditorWindow
{
    private const string MenuRoot = "toshi/VLiveKit/";
    private const string RegistryUrl = "https://registry.npmjs.org/";
    private const string CatalogPackageName = "com.toshi.vlivekit";
    private const string CatalogFileName = "package-catalog.json";
    private const string PromptPrefsKeyPrefix = "VLiveKitInstaller.PromptShown.";

    private static bool promptedThisSession;

    private readonly List<PackageRow> rows = new List<PackageRow>();
    private readonly Queue<PackageRow> pendingOperations = new Queue<PackageRow>();
    private readonly List<LatestVersionRequest> latestRequests = new List<LatestVersionRequest>();

    private Vector2 scrollPosition;
    private UnityWebRequest catalogMetadataRequest;
    private UnityWebRequest catalogRequest;
    private ListRequest listRequest;
    private AddRequest addRequest;
    private PackageRow activeOperation;
    private string activePackageId;
    private string activeDisplayName;
    private int operationTotalCount;
    private int operationCompletedCount;
    private GUIStyle headerStyle;
    private GUIStyle cardStyle;
    private GUIStyle badgeStyle;
    private GUIStyle mutedStyle;
    private GUIStyle toolbarButtonStyle;
    private GUIStyle titleBadgeStyle;
    private bool stylesReady;
    private string statusText = "Ready";
    private string catalogSource = "Bundled catalog";
    private bool isChecking;

    static VLiveKitInstallerWindow()
    {
        EditorApplication.delayCall += ShowInstallPromptOnce;
    }

    [MenuItem(MenuRoot + "Package Manager")]
    private static void OpenWindow()
    {
        var window = GetWindow<VLiveKitInstallerWindow>("VLiveKitPackageManager");
        window.minSize = new Vector2(900f, 520f);
        window.Show();
        window.Refresh();
    }

    [MenuItem(MenuRoot + "Check Install Status")]
    private static void CheckInstallStatus()
    {
        OpenWindow();
    }

    [MenuItem(MenuRoot + "Install Missing Packages")]
    private static void InstallMissingPackages()
    {
        var window = GetWindow<VLiveKitInstallerWindow>("VLiveKitPackageManager");
        window.minSize = new Vector2(900f, 520f);
        window.Show();
        window.RefreshAndInstallMissing();
    }

    [MenuItem(MenuRoot + "Open Installer Dialog")]
    private static void OpenInstallerDialog()
    {
        OpenWindow();
    }

    private static void ShowInstallPromptOnce()
    {
        if (promptedThisSession)
        {
            return;
        }

        promptedThisSession = true;
        var key = PromptPrefsKeyPrefix + Application.dataPath;
        if (EditorPrefs.GetBool(key, false))
        {
            return;
        }

        var result = EditorUtility.DisplayDialogComplex(
            "VLiveKitPackageManager",
            "Open VLiveKitPackageManager to check installed packages and available updates?",
            "Open",
            "Later",
            "Do Not Show Again");

        if (result == 0)
        {
            OpenWindow();
        }
        else if (result == 2)
        {
            EditorPrefs.SetBool(key, true);
        }
    }

    private void OnEnable()
    {
        EnsureRows();
        EditorApplication.update += UpdateRequests;
    }

    private void OnDisable()
    {
        EditorApplication.update -= UpdateRequests;
        DisposeLatestRequests();
        DisposeCatalogRequests();
        EditorUtility.ClearProgressBar();
    }

    private void OnGUI()
    {
        EnsureStyles();
        DrawHeader();
        DrawToolbar();
        DrawSummary();
        DrawPackageList();
        DrawFooter();
    }

    private void RefreshAndInstallMissing()
    {
        Refresh();
        statusText = "Checking before installing missing packages...";
        EditorApplication.delayCall += QueueMissingAfterRefresh;
    }

    private void QueueMissingAfterRefresh()
    {
        if (isChecking || listRequest != null || latestRequests.Count > 0)
        {
            EditorApplication.delayCall += QueueMissingAfterRefresh;
            return;
        }

        QueueRows(row => row.State == InstallState.Missing);
    }

    private void Refresh()
    {
        if (isChecking || addRequest != null)
        {
            return;
        }

        isChecking = true;
        statusText = "Refreshing package catalog...";
        rows.Clear();
        DisposeCatalogRequests();
        catalogMetadataRequest = UnityWebRequest.Get(RegistryUrl + CatalogPackageName);
        catalogMetadataRequest.timeout = 20;
        catalogMetadataRequest.SendWebRequest();
        Repaint();
    }

    private void StartStatusRefresh()
    {
        EnsureRows();
        statusText = "Checking installed packages...";
        foreach (var row in rows)
        {
            row.ResetRuntimeState();
        }

        listRequest = Client.List(true, false);
        Repaint();
    }

    private void UpdateRequests()
    {
        UpdateCatalogRequests();
        UpdateListRequest();
        UpdateLatestRequests();
        UpdateAddRequest();
    }

    private void UpdateCatalogRequests()
    {
        if (catalogMetadataRequest != null && catalogMetadataRequest.isDone)
        {
            if (catalogMetadataRequest.result == UnityWebRequest.Result.Success)
            {
                var tarballUrl = ParseLatestTarballUrl(catalogMetadataRequest.downloadHandler.text);
                if (!string.IsNullOrEmpty(tarballUrl))
                {
                    catalogRequest = UnityWebRequest.Get(tarballUrl);
                    catalogRequest.timeout = 20;
                    catalogRequest.SendWebRequest();
                    statusText = "Downloading latest package catalog...";
                }
                else
                {
                    LoadBundledCatalog("Latest catalog URL was not found.");
                    StartStatusRefresh();
                }
            }
            else
            {
                LoadBundledCatalog(catalogMetadataRequest.error);
                StartStatusRefresh();
            }

            catalogMetadataRequest.Dispose();
            catalogMetadataRequest = null;
            Repaint();
        }

        if (catalogRequest != null && catalogRequest.isDone)
        {
            if (catalogRequest.result == UnityWebRequest.Result.Success && TryLoadCatalogFromTarball(catalogRequest.downloadHandler.data, out var loadedRows))
            {
                rows.Clear();
                rows.AddRange(loadedRows);
                catalogSource = "Latest catalog";
            }
            else
            {
                LoadBundledCatalog(catalogRequest.error);
            }

            catalogRequest.Dispose();
            catalogRequest = null;
            StartStatusRefresh();
            Repaint();
        }
    }

    private void UpdateListRequest()
    {
        if (listRequest == null || !listRequest.IsCompleted)
        {
            return;
        }

        if (listRequest.Status == StatusCode.Success)
        {
            ApplyPackageCollection(listRequest.Result);
            statusText = "Checking latest versions...";
        }
        else
        {
            var errorMessage = listRequest.Error != null ? listRequest.Error.message : "Unknown Package Manager error.";
            Debug.LogWarning("VLiveKit package check failed. Falling back to manifest and local folders. " + errorMessage);
            ApplyPackageCollection(null);
            statusText = "Package Manager check failed; using fallback checks.";
        }

        listRequest = null;
        StartLatestVersionRequests();
        Repaint();
    }

    private void UpdateLatestRequests()
    {
        if (latestRequests.Count == 0)
        {
            return;
        }

        for (var i = latestRequests.Count - 1; i >= 0; i--)
        {
            var latestRequest = latestRequests[i];
            if (!latestRequest.Request.isDone)
            {
                continue;
            }

            latestRequest.Row.LatestCheckState = LatestState.Done;
            if (latestRequest.Request.result == UnityWebRequest.Result.Success)
            {
                latestRequest.Row.LatestVersion = ParseLatestVersion(latestRequest.Request.downloadHandler.text);
                if (string.IsNullOrEmpty(latestRequest.Row.LatestVersion))
                {
                    latestRequest.Row.LatestCheckState = LatestState.Failed;
                    latestRequest.Row.Message = "Latest version was not found.";
                }
            }
            else
            {
                latestRequest.Row.LatestCheckState = LatestState.Failed;
                latestRequest.Row.Message = latestRequest.Request.error;
            }

            latestRequest.Request.Dispose();
            latestRequests.RemoveAt(i);
        }

        if (latestRequests.Count == 0)
        {
            isChecking = false;
            statusText = "Status is up to date.";
        }

        Repaint();
    }

    private void UpdateAddRequest()
    {
        if (addRequest == null || !addRequest.IsCompleted)
        {
            return;
        }

        var completedRow = activeOperation ?? FindRowByPackageId(activePackageId);
        var displayName = !string.IsNullOrEmpty(activeDisplayName) ? activeDisplayName : activePackageId;
        operationCompletedCount++;

        if (addRequest.Status == StatusCode.Success)
        {
            if (completedRow != null)
            {
                completedRow.State = InstallState.PackageManager;
                completedRow.InstalledVersion = addRequest.Result.version;
                completedRow.Message = "Installed " + addRequest.Result.version;
            }

            statusText = "Installed " + displayName;
        }
        else
        {
            var errorMessage = addRequest.Error != null ? addRequest.Error.message : "Unknown Package Manager error.";
            if (completedRow != null)
            {
                completedRow.Message = errorMessage;
            }

            statusText = "Failed: " + displayName;
            Debug.LogError("Failed to install/update " + displayName + ": " + errorMessage);
        }

        addRequest = null;
        activeOperation = null;
        activePackageId = null;
        activeDisplayName = null;
        StartNextOperation();
        Repaint();
    }

    private void ApplyPackageCollection(PackageCollection packageCollection)
    {
        var packageVersions = new Dictionary<string, string>();
        if (packageCollection != null)
        {
            foreach (var packageInfo in packageCollection)
            {
                packageVersions[packageInfo.name] = packageInfo.version;
            }
        }

        var manifestJson = ReadManifestJson();
        foreach (var row in rows)
        {
            if (packageVersions.TryGetValue(row.Spec.PackageName, out var version))
            {
                row.State = InstallState.PackageManager;
                row.InstalledVersion = version;
                row.Message = "Installed by Package Manager";
                continue;
            }

            if (!string.IsNullOrEmpty(manifestJson) && manifestJson.Contains("\"" + row.Spec.PackageName + "\""))
            {
                row.State = InstallState.Manifest;
                row.InstalledVersion = ReadLocalPackageVersion(row.Spec) ?? "manifest";
                row.Message = "Found in manifest";
                continue;
            }

            if (Directory.Exists(ToProjectPath(row.Spec.PackageFolderPath)))
            {
                row.State = InstallState.LocalPackage;
                row.InstalledVersion = ReadLocalPackageVersion(row.Spec) ?? "local";
                row.Message = "Local package or submodule";
                continue;
            }

            if (AssetDatabase.IsValidFolder(row.Spec.AssetFolderPath) || Directory.Exists(ToProjectPath(row.Spec.AssetFolderPath)))
            {
                row.State = InstallState.AssetsFolder;
                row.InstalledVersion = "assets";
                row.Message = "Assets folder";
                continue;
            }

            row.State = InstallState.Missing;
            row.InstalledVersion = "";
            row.Message = "Not installed";
        }
    }

    private void StartLatestVersionRequests()
    {
        DisposeLatestRequests();

        foreach (var row in rows)
        {
            row.LatestCheckState = LatestState.Checking;
            var request = UnityWebRequest.Get(RegistryUrl + row.Spec.PackageName);
            request.timeout = 20;
            request.SendWebRequest();
            latestRequests.Add(new LatestVersionRequest(row, request));
        }
    }

    private void QueueRows(Predicate<PackageRow> predicate)
    {
        if (addRequest != null)
        {
            return;
        }

        pendingOperations.Clear();
        foreach (var row in rows)
        {
            if (predicate(row) && row.CanInstallFromRegistry)
            {
                pendingOperations.Enqueue(row);
            }
        }

        if (pendingOperations.Count == 0)
        {
            statusText = "Nothing to install or update.";
            Repaint();
            return;
        }

        operationTotalCount = pendingOperations.Count;
        operationCompletedCount = 0;
        StartNextOperation();
    }

    private void StartNextOperation()
    {
        if (addRequest != null)
        {
            return;
        }

        if (pendingOperations.Count == 0)
        {
            EditorUtility.ClearProgressBar();
            operationTotalCount = 0;
            operationCompletedCount = 0;
            Refresh();
            return;
        }

        activeOperation = pendingOperations.Dequeue();
        var version = string.IsNullOrEmpty(activeOperation.LatestVersion) ? "latest" : activeOperation.LatestVersion;
        var packageId = activeOperation.Spec.PackageName + "@" + version;
        activePackageId = activeOperation.Spec.PackageName;
        activeDisplayName = activeOperation.Spec.DisplayName;
        activeOperation.Message = "Adding " + packageId;
        statusText = GetOperationProgressLabel(activeOperation.Spec.DisplayName);
        EditorUtility.DisplayProgressBar("VLiveKitPackageManager", statusText, GetOperationProgress());
        addRequest = Client.Add(packageId);
    }

    private void EnsureRows()
    {
        if (rows.Count > 0)
        {
            return;
        }

        LoadBundledCatalog("");
    }

    private void EnsureStyles()
    {
        if (stylesReady)
        {
            return;
        }

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 20,
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.88f, 0.96f, 1f) : new Color(0.08f, 0.18f, 0.24f) }
        };
        cardStyle = new GUIStyle("box")
        {
            padding = new RectOffset(12, 12, 10, 10),
            margin = new RectOffset(8, 8, 5, 5)
        };
        badgeStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            padding = new RectOffset(8, 8, 2, 2)
        };
        mutedStyle = new GUIStyle(EditorStyles.label)
        {
            wordWrap = false,
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.62f, 0.68f, 0.72f) : new Color(0.35f, 0.38f, 0.42f) }
        };
        toolbarButtonStyle = new GUIStyle(EditorStyles.miniButton)
        {
            fixedHeight = 26
        };
        titleBadgeStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.82f, 0.95f, 1f) : new Color(0.08f, 0.16f, 0.22f) },
            padding = new RectOffset(8, 8, 2, 2)
        };
        stylesReady = true;
    }

    private void DrawHeader()
    {
        var rect = GUILayoutUtility.GetRect(0f, 104f, GUILayout.ExpandWidth(true));
        var topColor = EditorGUIUtility.isProSkin ? new Color(0.04f, 0.06f, 0.09f) : new Color(0.80f, 0.94f, 0.96f);
        var stageColor = EditorGUIUtility.isProSkin ? new Color(0.08f, 0.16f, 0.20f) : new Color(0.92f, 0.98f, 1f);
        EditorGUI.DrawRect(rect, topColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height * 0.52f, rect.width, rect.height * 0.48f), stageColor);

        DrawGlowBar(new Rect(rect.x, rect.y, rect.width, 3f), new Color(0.10f, 0.86f, 0.95f));
        DrawGlowBar(new Rect(rect.x, rect.y + rect.height - 3f, rect.width, 3f), new Color(0.92f, 0.20f, 0.55f));

        var spotlightWidth = Mathf.Min(220f, rect.width * 0.28f);
        EditorGUI.DrawRect(new Rect(rect.x + rect.width - spotlightWidth, rect.y, spotlightWidth, rect.height), new Color(0.18f, 0.25f, 0.42f, 0.28f));
        EditorGUI.DrawRect(new Rect(rect.x + rect.width - spotlightWidth * 0.48f, rect.y, spotlightWidth * 0.48f, rect.height), new Color(0.85f, 0.32f, 0.74f, 0.16f));

        var titleRect = new Rect(rect.x + 18f, rect.y + 14f, rect.width - 36f, 28f);
        GUI.Label(titleRect, "VLiveKitPackageManager", headerStyle);

        var subtitleRect = new Rect(rect.x + 18f, rect.y + 45f, rect.width - 36f, 20f);
        GUI.Label(subtitleRect, "Check versions, update packages, and jump to repositories or docs.", mutedStyle);

        var badgeRect = new Rect(rect.x + 18f, rect.y + 74f, 112f, 20f);
        EditorGUI.DrawRect(badgeRect, new Color(0.08f, 0.74f, 0.82f, 0.42f));
        GUI.Label(badgeRect, "LIVE TOOLKIT", titleBadgeStyle);

        var pulseRect = new Rect(badgeRect.xMax + 8f, badgeRect.y + 6f, rect.width - badgeRect.xMax - 32f, 6f);
        DrawSignalLine(pulseRect);
    }

    private static void DrawGlowBar(Rect rect, Color color)
    {
        EditorGUI.DrawRect(rect, color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y - 2f, rect.width, rect.height + 4f), new Color(color.r, color.g, color.b, 0.18f));
    }

    private static void DrawSignalLine(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.18f, 0.20f, 0.75f));
        var segments = Mathf.Max(1, Mathf.FloorToInt(rect.width / 48f));
        for (var i = 0; i < segments; i++)
        {
            var x = rect.x + i * 48f;
            var width = i % 2 == 0 ? 28f : 16f;
            var color = i % 3 == 0 ? new Color(0.10f, 0.86f, 0.95f) : i % 3 == 1 ? new Color(0.92f, 0.20f, 0.55f) : new Color(1f, 0.72f, 0.24f);
            EditorGUI.DrawRect(new Rect(x, rect.y, Mathf.Min(width, rect.xMax - x), rect.height), color);
        }
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUI.enabled = !isChecking && addRequest == null;
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80f)))
        {
            Refresh();
        }

        if (GUILayout.Button("Install Missing", EditorStyles.toolbarButton, GUILayout.Width(110f)))
        {
            QueueRows(row => row.State == InstallState.Missing);
        }

        if (GUILayout.Button("Update All", EditorStyles.toolbarButton, GUILayout.Width(90f)))
        {
            QueueRows(row => row.CanUpdate);
        }

        if (GUILayout.Button("Logs", EditorStyles.toolbarButton, GUILayout.Width(64f)))
        {
            VLiveKitErrorLogExporter.OpenWindow();
        }

        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        GUILayout.Label(catalogSource + " - " + statusText, mutedStyle);
        EditorGUILayout.EndHorizontal();

        if (IsOperating)
        {
            var rect = GUILayoutUtility.GetRect(1f, 4f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));
            var fillRect = new Rect(rect.x, rect.y, rect.width * GetOperationProgress(), rect.height);
            EditorGUI.DrawRect(fillRect, new Color(0.24f, 0.62f, 0.92f));
        }
    }

    private void DrawSummary()
    {
        var installed = 0;
        var missing = 0;
        var updates = 0;
        var local = 0;

        foreach (var row in rows)
        {
            if (row.State == InstallState.Missing)
            {
                missing++;
            }
            else
            {
                installed++;
            }

            if (row.CanUpdate)
            {
                updates++;
            }

            if (row.IsLocal)
            {
                local++;
            }
        }

        EditorGUILayout.BeginHorizontal();
        DrawMetric("Installed", installed.ToString(), new Color(0.25f, 0.68f, 0.45f));
        DrawMetric("Updates", updates.ToString(), new Color(0.95f, 0.58f, 0.18f));
        DrawMetric("Missing", missing.ToString(), new Color(0.88f, 0.28f, 0.25f));
        DrawMetric("Local", local.ToString(), new Color(0.35f, 0.55f, 0.90f));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMetric(string label, string value, Color accent)
    {
        EditorGUILayout.BeginVertical(cardStyle, GUILayout.Height(62f));
        var rect = GUILayoutUtility.GetRect(120f, 8f, GUILayout.ExpandWidth(true));
        DrawGlowBar(rect, accent);
        GUILayout.Label(value, new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, alignment = TextAnchor.MiddleCenter });
        GUILayout.Label(label, new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleCenter });
        EditorGUILayout.EndVertical();
    }

    private void DrawPackageList()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        foreach (var row in rows)
        {
            DrawPackageRow(row);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawPackageRow(PackageRow row)
    {
        EditorGUILayout.BeginVertical(cardStyle);
        var accentRect = GUILayoutUtility.GetRect(1f, 2f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(accentRect, GetRowAccentColor(row));
        EditorGUILayout.BeginHorizontal();

        DrawStatusBadge(row);

        EditorGUILayout.BeginVertical();
        GUILayout.Label(row.Spec.DisplayName, EditorStyles.boldLabel);
        GUILayout.Label(row.Spec.PackageName, mutedStyle);
        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        DrawVersionBlock("Installed", string.IsNullOrEmpty(row.InstalledVersion) ? "-" : row.InstalledVersion);
        DrawVersionBlock("Latest", row.LatestLabel);

        if (GUILayout.Button("GitHub", toolbarButtonStyle, GUILayout.Width(72f)))
        {
            Application.OpenURL(row.Spec.RepositoryUrl);
        }

        if (GUILayout.Button("Docs", toolbarButtonStyle, GUILayout.Width(58f)))
        {
            Application.OpenURL(row.Spec.DocumentationUrl);
        }

        GUI.enabled = addRequest == null && !isChecking && row.CanInstallFromRegistry;
        if (row.State == InstallState.Missing)
        {
            if (GUILayout.Button("Install", toolbarButtonStyle, GUILayout.Width(78f)))
            {
                QueueRows(candidate => candidate == row);
            }
        }
        else if (row.CanUpdate)
        {
            if (GUILayout.Button("Update", toolbarButtonStyle, GUILayout.Width(78f)))
            {
                QueueRows(candidate => candidate == row);
            }
        }
        else
        {
            GUI.enabled = false;
            GUILayout.Button(row.IsLocal ? "Local" : "Current", toolbarButtonStyle, GUILayout.Width(78f));
        }

        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(row.Message))
        {
            GUILayout.Space(3f);
            GUILayout.Label(row.Message, mutedStyle);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawStatusBadge(PackageRow row)
    {
        var label = row.StatusLabel;
        var color = row.StatusColor;
        var rect = GUILayoutUtility.GetRect(96f, 24f, GUILayout.Width(96f), GUILayout.Height(24f));
        EditorGUI.DrawRect(rect, color);
        GUI.Label(rect, label, badgeStyle);
    }

    private static Color GetRowAccentColor(PackageRow row)
    {
        if (row.CanUpdate)
        {
            return new Color(1f, 0.58f, 0.16f);
        }

        if (row.State == InstallState.Missing)
        {
            return new Color(0.95f, 0.22f, 0.28f);
        }

        if (row.IsLocal)
        {
            return new Color(0.38f, 0.56f, 0.96f);
        }

        return new Color(0.08f, 0.78f, 0.58f);
    }

    private void DrawVersionBlock(string label, string value)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(90f));
        GUILayout.Label(label, mutedStyle);
        GUILayout.Label(value, EditorStyles.boldLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawFooter()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label("Local packages and Assets folders are detected but not overwritten automatically.", mutedStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Open manifest", EditorStyles.miniButton, GUILayout.Width(100f)))
        {
            var manifestPath = ToProjectPath("Packages/manifest.json");
            if (File.Exists(manifestPath))
            {
                EditorUtility.OpenWithDefaultApp(manifestPath);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private bool IsOperating => addRequest != null || pendingOperations.Count > 0;

    private float GetOperationProgress()
    {
        if (operationTotalCount <= 0)
        {
            return 0f;
        }

        return Mathf.Clamp01((operationCompletedCount + (addRequest != null ? 0.35f : 0f)) / operationTotalCount);
    }

    private string GetOperationProgressLabel(string displayName)
    {
        var total = Mathf.Max(operationTotalCount, 1);
        var current = Mathf.Min(operationCompletedCount + 1, total);
        return "Updating " + displayName + " (" + current + "/" + total + ")";
    }

    private PackageRow FindRowByPackageId(string packageId)
    {
        if (string.IsNullOrEmpty(packageId))
        {
            return null;
        }

        foreach (var row in rows)
        {
            if (row.Spec.PackageName == packageId)
            {
                return row;
            }
        }

        return null;
    }

    private void DisposeLatestRequests()
    {
        foreach (var latestRequest in latestRequests)
        {
            latestRequest.Request.Dispose();
        }

        latestRequests.Clear();
    }

    private void DisposeCatalogRequests()
    {
        if (catalogMetadataRequest != null)
        {
            catalogMetadataRequest.Dispose();
            catalogMetadataRequest = null;
        }

        if (catalogRequest != null)
        {
            catalogRequest.Dispose();
            catalogRequest = null;
        }
    }

    private static string ParseLatestVersion(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return string.Empty;
        }

        var match = Regex.Match(json, "\"latest\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string ParseLatestTarballUrl(string json)
    {
        var latestVersion = ParseLatestVersion(json);
        if (string.IsNullOrEmpty(latestVersion))
        {
            return string.Empty;
        }

        var escapedVersion = Regex.Escape(latestVersion);
        var versionBlock = Regex.Match(json, "\"" + escapedVersion + "\"\\s*:\\s*\\{.*?\"dist\"\\s*:\\s*\\{.*?\"tarball\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Singleline);
        return versionBlock.Success ? versionBlock.Groups[1].Value.Replace("\\/", "/") : string.Empty;
    }

    private void LoadBundledCatalog(string reason)
    {
        rows.Clear();
        var catalogPath = FindBundledCatalogPath();
        if (!string.IsNullOrEmpty(catalogPath) && File.Exists(catalogPath) && TryLoadCatalogJson(File.ReadAllText(catalogPath), out var loadedRows))
        {
            rows.AddRange(loadedRows);
            catalogSource = "Bundled catalog";
            statusText = string.IsNullOrEmpty(reason) ? "Using bundled catalog." : "Using bundled catalog: " + reason;
            return;
        }

        LoadFallbackCatalog();
        catalogSource = "Fallback catalog";
        statusText = "Using fallback catalog.";
    }

    private static string FindBundledCatalogPath()
    {
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VLiveKitInstallerWindow).Assembly);
        if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
        {
            var packageCatalog = Path.Combine(packageInfo.resolvedPath, CatalogFileName);
            if (File.Exists(packageCatalog))
            {
                return packageCatalog;
            }
        }

        var projectCatalog = ToProjectPath("Packages/VLiveKit/" + CatalogFileName);
        return File.Exists(projectCatalog) ? projectCatalog : null;
    }

    private static bool TryLoadCatalogFromTarball(byte[] data, out List<PackageRow> loadedRows)
    {
        loadedRows = null;
        if (data == null || data.Length == 0)
        {
            return false;
        }

        try
        {
            var json = ReadFileFromTgz(data, "package/" + CatalogFileName);
            return !string.IsNullOrEmpty(json) && TryLoadCatalogJson(json, out loadedRows);
        }
        catch
        {
            return false;
        }
    }

    private static string ReadFileFromTgz(byte[] data, string path)
    {
        using (var compressedStream = new MemoryStream(data))
        using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
        using (var tarStream = new MemoryStream())
        {
            gzipStream.CopyTo(tarStream);
            tarStream.Position = 0;

            while (tarStream.Position + 512 <= tarStream.Length)
            {
                var header = new byte[512];
                var read = tarStream.Read(header, 0, header.Length);
                if (read < header.Length || IsEmptyTarBlock(header))
                {
                    break;
                }

                var name = ReadTarString(header, 0, 100);
                var prefix = ReadTarString(header, 345, 155);
                if (!string.IsNullOrEmpty(prefix))
                {
                    name = prefix + "/" + name;
                }

                var sizeText = ReadTarString(header, 124, 12).Trim();
                var size = string.IsNullOrEmpty(sizeText) ? 0L : Convert.ToInt64(sizeText, 8);
                var dataPosition = tarStream.Position;

                if (name == path)
                {
                    var fileData = new byte[size];
                    tarStream.Read(fileData, 0, fileData.Length);
                    return Encoding.UTF8.GetString(fileData);
                }

                tarStream.Position = dataPosition + RoundUpToTarBlock(size);
            }
        }

        return null;
    }

    private static bool IsEmptyTarBlock(byte[] block)
    {
        for (var i = 0; i < block.Length; i++)
        {
            if (block[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string ReadTarString(byte[] data, int offset, int count)
    {
        var length = 0;
        while (length < count && data[offset + length] != 0)
        {
            length++;
        }

        return Encoding.UTF8.GetString(data, offset, length);
    }

    private static long RoundUpToTarBlock(long size)
    {
        return ((size + 511L) / 512L) * 512L;
    }

    private static bool TryLoadCatalogJson(string json, out List<PackageRow> loadedRows)
    {
        loadedRows = new List<PackageRow>();
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        PackageCatalogData catalog;
        try
        {
            catalog = JsonUtility.FromJson<PackageCatalogData>(json);
        }
        catch
        {
            return false;
        }

        if (catalog == null || catalog.packages == null)
        {
            return false;
        }

        foreach (var item in catalog.packages)
        {
            if (item == null ||
                string.IsNullOrEmpty(item.name) ||
                string.IsNullOrEmpty(item.displayName) ||
                string.IsNullOrEmpty(item.packageFolderPath) ||
                string.IsNullOrEmpty(item.assetFolderPath))
            {
                continue;
            }

            var repositoryUrl = string.IsNullOrEmpty(item.repositoryUrl) ? BuildFallbackRepositoryUrl(item.name) : item.repositoryUrl;
            var documentationUrl = string.IsNullOrEmpty(item.documentationUrl) ? repositoryUrl + "#readme" : item.documentationUrl;
            loadedRows.Add(new PackageRow(new PackageSpec(
                item.name,
                item.displayName,
                item.packageFolderPath,
                item.assetFolderPath,
                repositoryUrl,
                documentationUrl)));
        }

        return loadedRows.Count > 0;
    }

    private void LoadFallbackCatalog()
    {
        rows.Clear();
        rows.Add(new PackageRow(new PackageSpec("com.toshi.vlivekit.artnetlink", "VLiveKit ArtNetLink", "Packages/VLiveKit_ArtNetLink", "Assets/toshi.VLiveKit/ArtNetLink", "https://github.com/toshi-kundesu/VLiveKit_ArtNetLink", "https://github.com/toshi-kundesu/VLiveKit_ArtNetLink#readme")));
        rows.Add(new PackageRow(new PackageSpec("com.toshi.vlivekit.cameraunit", "VLive Camera Unit", "Packages/VLiveKit_camera", "Assets/toshi.VLiveKit/VLiveCameraUnit", "https://github.com/toshi-kundesu/VLiveCameraUnit", "https://github.com/toshi-kundesu/VLiveCameraUnit#readme")));
        rows.Add(new PackageRow(new PackageSpec("com.toshi.vlivekit.ledvision", "VLiveKit LED Vision", "Packages/VLiveKit_LEDVision", "Assets/toshi.VLiveKit/LEDVision", "https://github.com/toshi-kundesu/VLiveKit_LEDVision", "https://github.com/toshi-kundesu/VLiveKit_LEDVision#readme")));
        rows.Add(new PackageRow(new PackageSpec("com.toshi.vlivekit.lensfilters", "VLive Lens Filters", "Packages/VLiveKit_LiveLensFilters", "Assets/toshi.VLiveKit/LiveLensFilters", "https://github.com/toshi-kundesu/VLiveKit_LiveLensFilters", "https://github.com/toshi-kundesu/VLiveKit_LiveLensFilters#readme")));
        rows.Add(new PackageRow(new PackageSpec("com.toshi.vlivekit.livetoon", "VLive Live Toon", "Packages/VLiveKit_LiveToon", "Assets/toshi.VLiveKit/livetoon", "https://github.com/toshi-kundesu/VLiveKit_livetoon", "https://github.com/toshi-kundesu/VLiveKit_livetoon#readme")));
        rows.Add(new PackageRow(new PackageSpec("com.toshi.vlivekit.performeract", "VLive Performer Act", "Packages/VLiveKit_PerformerAct", "Assets/toshi.VLiveKit/PerformerAct", "https://github.com/toshi-kundesu/VLiveKit_PerformerAct", "https://github.com/toshi-kundesu/VLiveKit_PerformerAct#readme")));
        rows.Add(new PackageRow(new PackageSpec("com.toshi.vlivekit.testassetscontainer", "VLiveKit Test Assets Container", "Packages/VLiveKit_TestAssetsContainer", "Assets/toshi.VLiveKit/TestAssetsContainer", "https://github.com/toshi-kundesu/VLiveKit_TestAssetsContainer", "https://github.com/toshi-kundesu/VLiveKit_TestAssetsContainer#readme")));
        rows.Add(new PackageRow(new PackageSpec("com.toshi.vlivekit.stagebuilder", "VLiveKit StageBuilder", "Packages/VLiveKit_StageBuilder", "Assets/toshi.VLiveKit/StageBuilder", "https://github.com/toshi-kundesu/VLiveKit_StageBuilder", "https://github.com/toshi-kundesu/VLiveKit_StageBuilder#readme")));
        rows.Add(new PackageRow(new PackageSpec("com.toshi.vlivekit.stageeffect", "VLiveKit StageEffect", "Packages/VLiveKit_StageEffect", "Assets/toshi.VLiveKit/StageEffect", "https://github.com/toshi-kundesu/VLiveKit_StageEffect", "https://github.com/toshi-kundesu/VLiveKit_StageEffect#readme")));
        rows.Add(new PackageRow(new PackageSpec("com.toshi.vlivekit.videorack", "VLiveKit VideoRack", "Packages/VLiveKit_VideoRack", "Assets/toshi.VLiveKit/VideoRack", "https://github.com/toshi-kundesu/VLiveKit_VideoRack", "https://github.com/toshi-kundesu/VLiveKit_VideoRack#readme")));
    }

    private static string ReadManifestJson()
    {
        var manifestPath = ToProjectPath("Packages/manifest.json");
        return File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : string.Empty;
    }

    private static string ReadLocalPackageVersion(PackageSpec package)
    {
        var packageJsonPath = FindLocalPackageJson(package);
        if (string.IsNullOrEmpty(packageJsonPath) || !File.Exists(packageJsonPath))
        {
            return null;
        }

        var json = File.ReadAllText(packageJsonPath);
        var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string BuildFallbackRepositoryUrl(string packageName)
    {
        return "https://www.npmjs.com/package/" + packageName;
    }

    private static string FindLocalPackageJson(PackageSpec package)
    {
        var assetPath = ToProjectPath(package.AssetFolderPath + "/package.json");
        if (File.Exists(assetPath))
        {
            return assetPath;
        }

        var packageFolder = ToProjectPath(package.PackageFolderPath);
        if (!Directory.Exists(packageFolder))
        {
            return null;
        }

        var candidates = Directory.GetFiles(packageFolder, "package.json", SearchOption.AllDirectories);
        foreach (var candidate in candidates)
        {
            if (candidate.IndexOf(Path.DirectorySeparatorChar + "Library" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static string ToProjectPath(string unityRelativePath)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", unityRelativePath));
    }

    private static int CompareVersions(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return 0;
        }

        if (Version.TryParse(StripPrerelease(left), out var leftVersion) && Version.TryParse(StripPrerelease(right), out var rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.CompareOrdinal(left, right);
    }

    private static string StripPrerelease(string version)
    {
        var dashIndex = version.IndexOf('-');
        return dashIndex >= 0 ? version.Substring(0, dashIndex) : version;
    }

    private readonly struct PackageSpec
    {
        public PackageSpec(string packageName, string displayName, string packageFolderPath, string assetFolderPath, string repositoryUrl, string documentationUrl)
        {
            PackageName = packageName;
            DisplayName = displayName;
            PackageFolderPath = packageFolderPath;
            AssetFolderPath = assetFolderPath;
            RepositoryUrl = repositoryUrl;
            DocumentationUrl = documentationUrl;
        }

        public string PackageName { get; }
        public string DisplayName { get; }
        public string PackageFolderPath { get; }
        public string AssetFolderPath { get; }
        public string RepositoryUrl { get; }
        public string DocumentationUrl { get; }
    }

    [Serializable]
    private sealed class PackageCatalogData
    {
        public PackageCatalogItem[] packages;
    }

    [Serializable]
    private sealed class PackageCatalogItem
    {
        public string name;
        public string displayName;
        public string packageFolderPath;
        public string assetFolderPath;
        public string repositoryUrl;
        public string documentationUrl;
    }

    private sealed class PackageRow
    {
        public PackageRow(PackageSpec spec)
        {
            Spec = spec;
            ResetRuntimeState();
        }

        public PackageSpec Spec { get; }
        public InstallState State { get; set; }
        public LatestState LatestCheckState { get; set; }
        public string InstalledVersion { get; set; }
        public string LatestVersion { get; set; }
        public string Message { get; set; }

        public bool IsLocal => State == InstallState.LocalPackage || State == InstallState.AssetsFolder;
        public bool CanInstallFromRegistry => !IsLocal && LatestCheckState != LatestState.Checking;
        public bool CanUpdate => State != InstallState.Missing && !IsLocal && !string.IsNullOrEmpty(InstalledVersion) && !string.IsNullOrEmpty(LatestVersion) && CompareVersions(InstalledVersion, LatestVersion) < 0;

        public string LatestLabel
        {
            get
            {
                if (LatestCheckState == LatestState.Checking)
                {
                    return "...";
                }

                return string.IsNullOrEmpty(LatestVersion) ? "-" : LatestVersion;
            }
        }

        public string StatusLabel
        {
            get
            {
                if (State == InstallState.Missing)
                {
                    return "Missing";
                }

                if (CanUpdate)
                {
                    return "Update";
                }

                if (IsLocal)
                {
                    return "Local";
                }

                return "Current";
            }
        }

        public Color StatusColor
        {
            get
            {
                if (State == InstallState.Missing)
                {
                    return new Color(0.72f, 0.18f, 0.16f);
                }

                if (CanUpdate)
                {
                    return new Color(0.88f, 0.45f, 0.12f);
                }

                if (IsLocal)
                {
                    return new Color(0.22f, 0.42f, 0.72f);
                }

                return new Color(0.18f, 0.58f, 0.32f);
            }
        }

        public void ResetRuntimeState()
        {
            State = InstallState.Missing;
            LatestCheckState = LatestState.Idle;
            InstalledVersion = "";
            LatestVersion = "";
            Message = "";
        }
    }

    private sealed class LatestVersionRequest
    {
        public LatestVersionRequest(PackageRow row, UnityWebRequest request)
        {
            Row = row;
            Request = request;
        }

        public PackageRow Row { get; }
        public UnityWebRequest Request { get; }
    }

    private enum InstallState
    {
        Missing,
        PackageManager,
        Manifest,
        LocalPackage,
        AssetsFolder
    }

    private enum LatestState
    {
        Idle,
        Checking,
        Done,
        Failed
    }
}
#endif
