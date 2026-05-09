#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.UI;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

[InitializeOnLoad]
internal sealed class VLiveKitInstallerWindow : EditorWindow
{
    private const string MenuRoot = "toshi/VLiveKit/Package Manager/";
    private const string RegistryUrl = "https://registry.npmjs.org/";
    private const string CatalogPackageName = "com.toshi.vlivekit";
    private const string LensFiltersPackageName = "com.toshi.vlivekit.lensfilters";
    private const string CatalogFileName = "package-catalog.json";
    private const string ThirdPartyAssetsRepositoryName = "VLiveKit_ThirdPartyAssets";
    private const string ThirdPartyUtilitiesPackageName = "com.toshi.vlivekit.thirdpartyutilities";
    private const string PromptPrefsKeyPrefix = "VLiveKitInstaller.PromptShown.";
    private const string HDRenderPipelineGlobalSettingsTypeName = "UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings, Unity.RenderPipelines.HighDefinition.Runtime";
    private const string HDRPBeforePostProcessPropertyName = "beforePostProcessCustomPostProcesses";
    private const string HDRPAfterPostProcessPropertyName = "afterPostProcessCustomPostProcesses";

    private static readonly RecommendedPostProcessType[] RecommendedBeforePostProcesses =
    {
        new RecommendedPostProcessType("UnityEngine.Rendering.HighDefinition.Compositor.ChromaKeying", "Unity.RenderPipelines.HighDefinition.Runtime"),
        new RecommendedPostProcessType("UnityEngine.Rendering.HighDefinition.Compositor.AlphaInjection", "Unity.RenderPipelines.HighDefinition.Runtime"),
        new RecommendedPostProcessType("Kino.PostProcessing.diffusion", "Kino.Postprocessing"),
        new RecommendedPostProcessType("Kino.PostProcessing.StreakTest", "Kino.Postprocessing"),
        new RecommendedPostProcessType("Kino.PostProcessing.GenshinColorGrading", "Kino.Postprocessing"),
        new RecommendedPostProcessType("Kino.PostProcessing.Streak", "Kino.Postprocessing"),
        new RecommendedPostProcessType("Kino.PostProcessing.MyPostFx", "Kino.Postprocessing")
    };

    private static readonly RecommendedPostProcessType[] RecommendedAfterPostProcesses =
    {
        new RecommendedPostProcessType("Kino.PostProcessing.GenshinBloom", "Kino.Postprocessing"),
        new RecommendedPostProcessType("Kino.PostProcessing.Recolor", "Kino.Postprocessing"),
        new RecommendedPostProcessType("Kino.PostProcessing.Glitch", "Kino.Postprocessing"),
        new RecommendedPostProcessType("Kino.PostProcessing.Utility", "Kino.Postprocessing"),
        new RecommendedPostProcessType("Kino.PostProcessing.Slice", "Kino.Postprocessing"),
        new RecommendedPostProcessType("Kino.PostProcessing.Sharpen", "Kino.Postprocessing"),
        new RecommendedPostProcessType("Kino.PostProcessing.Overlay", "Kino.Postprocessing"),
        new RecommendedPostProcessType("Kino.PostProcessing.TestCard", "Kino.Postprocessing")
    };

    private static bool promptedThisSession;

    private readonly List<PackageRow> rows = new List<PackageRow>();
    private readonly Queue<PackageRow> pendingOperations = new Queue<PackageRow>();
    private readonly List<LatestVersionRequest> latestRequests = new List<LatestVersionRequest>();

    private Vector2 scrollPosition;
    private UnityWebRequest catalogMetadataRequest;
    private UnityWebRequest catalogRequest;
    private ListRequest listRequest;
    private AddRequest addRequest;
    private RemoveRequest removeRequest;
    private PackageRow activeOperation;
    private string activePackageId;
    private string activeDisplayName;
    private PackageRow activeRemoveOperation;
    private string activeRemovePackageId;
    private string activeRemoveDisplayName;
    private int operationTotalCount;
    private int operationCompletedCount;
    private int operationFailedCount;
    private GUIStyle headerStyle;
    private GUIStyle cardStyle;
    private GUIStyle badgeStyle;
    private GUIStyle mutedStyle;
    private GUIStyle toolbarButtonStyle;
    private GUIStyle primaryButtonStyle;
    private GUIStyle metricValueStyle;
    private GUIStyle positiveNoticeStyle;
    private GUIStyle rowTitleStyle;
    private GUIStyle tableHeaderStyle;
    private bool stylesReady;
    private string statusText = "Ready";
    private string catalogSource = "Bundled catalog";
    private bool isChecking;

    static VLiveKitInstallerWindow()
    {
        EditorApplication.delayCall += ShowInstallPromptOnce;
    }

    [MenuItem(MenuRoot + "Open")]
    private static void OpenWindow()
    {
        VLiveKitManifestUtility.EnsureVLiveKitScopedRegistry();
        var window = GetWindow<VLiveKitInstallerWindow>("VLiveKitPackageManager");
        window.minSize = new Vector2(900f, 520f);
        window.Show();
        window.Refresh();
    }

    [MenuItem("toshi/VLiveKit Package Manager")]
    private static void OpenInstallerShortcut()
    {
        OpenWindow();
    }

    [MenuItem("toshi/LensFilters/Recommended HDRP Volume Settings...")]
    private static void OpenRecommendedHDRPVolumeSettingsFromMenu()
    {
        OpenRecommendedHDRPVolumeSettingsWindow();
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

        FirstRunPromptWindow.Open(key, !ProjectHasGitMetadata(), OpenWindow);
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
        DrawOperationProgress();
        DrawBackupNotice();
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

    private void RefreshAndInstallAll()
    {
        Refresh();
        statusText = "Checking before installing all packages...";
        EditorApplication.delayCall += QueueAllAfterRefresh;
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

    private void QueueAllAfterRefresh()
    {
        if (isChecking || listRequest != null || latestRequests.Count > 0)
        {
            EditorApplication.delayCall += QueueAllAfterRefresh;
            return;
        }

        QueueRows(IsInstallAllCandidate);
    }

    private void Refresh()
    {
        if (isChecking || addRequest != null || removeRequest != null)
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
        UpdateRemoveRequest();
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
                EnsureInstallerRow();
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
            var errorMessage = GetPackageManagerErrorMessage(listRequest.Error);
            ApplyPackageCollection(null);
            statusText = "Using manifest and local folder checks. Package Manager did not answer this time.";
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.Message))
                {
                    row.Message = "Checked with project files. Refresh again if Unity Package Manager is still loading.";
                }
            }

            Debug.Log("VLiveKitPackageManager used project-file fallback checks. " + errorMessage);
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
        var displayName = !string.IsNullOrEmpty(activeDisplayName) ? activeDisplayName : (!string.IsNullOrEmpty(activePackageId) ? activePackageId : "the package");
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
            operationFailedCount++;
            var errorMessage = GetPackageManagerErrorMessage(addRequest.Error);
            if (completedRow != null)
            {
                completedRow.Message = "Could not install/update. " + errorMessage;
            }

            statusText = "Could not install/update " + displayName;
            Debug.Log("VLiveKitPackageManager could not install/update " + displayName + ". " + errorMessage);
        }

        addRequest = null;
        activeOperation = null;
        activePackageId = null;
        activeDisplayName = null;
        StartNextOperation();
        Repaint();
    }

    private void UpdateRemoveRequest()
    {
        if (removeRequest == null || !removeRequest.IsCompleted)
        {
            return;
        }

        var completedRow = activeRemoveOperation ?? FindRowByPackageId(activeRemovePackageId);
        var displayName = !string.IsNullOrEmpty(activeRemoveDisplayName) ? activeRemoveDisplayName : (!string.IsNullOrEmpty(activeRemovePackageId) ? activeRemovePackageId : "the package");

        if (removeRequest.Status == StatusCode.Success)
        {
            if (completedRow != null)
            {
                completedRow.State = InstallState.Missing;
                completedRow.InstalledVersion = "";
                completedRow.Message = "Uninstalled";
            }

            statusText = "Uninstalled " + displayName;
        }
        else
        {
            var errorMessage = GetPackageManagerErrorMessage(removeRequest.Error);
            if (completedRow != null)
            {
                completedRow.Message = "Could not uninstall. " + errorMessage;
            }

            statusText = "Could not uninstall " + displayName;
            Debug.Log("VLiveKitPackageManager could not uninstall " + displayName + ". " + errorMessage);
        }

        removeRequest = null;
        activeRemoveOperation = null;
        activeRemovePackageId = null;
        activeRemoveDisplayName = null;
        EditorUtility.ClearProgressBar();
        Refresh();
        Repaint();
    }

    private void ApplyPackageCollection(PackageCollection packageCollection)
    {
        var packageInfos = new Dictionary<string, UnityEditor.PackageManager.PackageInfo>();
        if (packageCollection != null)
        {
            foreach (var packageInfo in packageCollection)
            {
                packageInfos[packageInfo.name] = packageInfo;
            }
        }

        var manifestJson = ReadManifestJson();
        foreach (var row in rows)
        {
            var manifestDependency = string.Empty;
            if (!string.IsNullOrEmpty(manifestJson))
            {
                var manifestMatch = Regex.Match(manifestJson, "\"" + Regex.Escape(row.Spec.PackageName) + "\"\\s*:\\s*\"([^\"]+)\"");
                manifestDependency = manifestMatch.Success ? manifestMatch.Groups[1].Value : string.Empty;
            }

            if (packageInfos.TryGetValue(row.Spec.PackageName, out var packageInfo))
            {
                if (TryApplyLocalPackageManagerInfo(row, packageInfo))
                {
                    continue;
                }

                row.State = InstallState.PackageManager;
                row.InstalledVersion = packageInfo.version;
                row.Message = "Installed by Package Manager";
                continue;
            }

            if (!string.IsNullOrEmpty(manifestDependency) &&
                manifestDependency.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                row.State = InstallState.LocalPackage;
                row.InstalledVersion = ReadLocalPackageVersion(row.Spec) ?? "local";
                row.Message = IsSubmodulePackage(row.Spec) ? "Installed by submodule" : "Local package folder";
                continue;
            }

            if (!string.IsNullOrEmpty(manifestDependency))
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
                row.Message = IsSubmodulePackage(row.Spec) ? "Installed by submodule" : "Local package folder";
                continue;
            }

            if (row.Spec.PackageName == CatalogPackageName && IsInstallerSourcePresentInProject())
            {
                row.State = InstallState.LocalPackage;
                row.InstalledVersion = ReadLocalPackageVersion(row.Spec) ?? ReadInstallerPackageVersion() ?? "local";
                row.Message = "Installer source in project";
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

    private static bool TryApplyLocalPackageManagerInfo(PackageRow row, UnityEditor.PackageManager.PackageInfo packageInfo)
    {
        if (!IsLocalPackageManagerSource(packageInfo))
        {
            return false;
        }

        row.State = InstallState.LocalPackage;
        row.InstalledVersion = ReadResolvedPackageVersion(packageInfo) ??
            ReadLocalPackageVersion(row.Spec) ??
            (!string.IsNullOrEmpty(packageInfo.version) ? packageInfo.version : "local");
        row.Message = IsSubmodulePackage(row.Spec) ? "Installed by submodule" : "Local package folder";
        return true;
    }

    private static bool IsLocalPackageManagerSource(UnityEditor.PackageManager.PackageInfo packageInfo)
    {
        return packageInfo != null &&
            (packageInfo.source == UnityEditor.PackageManager.PackageSource.Local ||
                packageInfo.source == UnityEditor.PackageManager.PackageSource.Embedded ||
                packageInfo.source == UnityEditor.PackageManager.PackageSource.LocalTarball);
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
        if (addRequest != null || removeRequest != null)
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
        operationFailedCount = 0;
        StartNextOperation();
    }

    private static bool IsInstallAllCandidate(PackageRow row)
    {
        return row.Spec.PackageName != CatalogPackageName &&
            !IsBlockedInstallTarget(row.Spec) &&
            (row.State == InstallState.Missing || row.CanUpdate);
    }

    private static void LogOperationFinished(int completed, int failed)
    {
        if (completed <= 0)
        {
            return;
        }

        if (failed == 0)
        {
            var packageText = completed == 1 ? "package" : "packages";
            Debug.Log("VLiveKit finished installing/updating " + completed + " " + packageText + ".");
            return;
        }

        var succeeded = Mathf.Max(0, completed - failed);
        Debug.Log("VLiveKit completed " + succeeded + " operation(s). " + failed + " operation(s) need another Refresh after Unity finishes compiling or importing.");
    }

    private static void ShowSoftNotice(string message)
    {
        Debug.Log(message);
        var window = GetWindow<VLiveKitInstallerWindow>("VLiveKitPackageManager");
        window.ShowNotification(new GUIContent(message));
    }

    private static string GetPackageManagerErrorMessage(Error error)
    {
        if (error != null && !string.IsNullOrEmpty(error.message))
        {
            return error.message;
        }

        return "Unity Package Manager did not return details. It may still be resolving, compiling, or importing assets.";
    }

    private void StartNextOperation()
    {
        if (addRequest != null)
        {
            return;
        }

        if (pendingOperations.Count == 0)
        {
            var completed = operationCompletedCount;
            var failed = operationFailedCount;
            EditorUtility.ClearProgressBar();
            operationTotalCount = 0;
            operationCompletedCount = 0;
            operationFailedCount = 0;
            LogOperationFinished(completed, failed);
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
        if (VLiveKitManifestUtility.EnsureVLiveKitScopedRegistry())
        {
            statusText = "Updated VLiveKit scoped registry. " + statusText;
        }

        if (EnsureExternalScopedRegistry())
        {
            statusText = "Updated scoped registries. " + statusText;
        }

        EditorUtility.DisplayProgressBar("VLiveKitPackageManager", statusText, GetOperationProgress());
        addRequest = Client.Add(packageId);
    }

    private void UninstallRow(PackageRow row)
    {
        if (row == null || addRequest != null || removeRequest != null || isChecking || !row.CanUninstall)
        {
            return;
        }

        if (!EditorUtility.DisplayDialog(
            "Uninstall VLiveKit package",
            "Remove " + row.Spec.DisplayName + " from Package Manager dependencies?",
            "Uninstall",
            "Cancel"))
        {
            return;
        }

        activeRemoveOperation = row;
        activeRemovePackageId = row.Spec.PackageName;
        activeRemoveDisplayName = row.Spec.DisplayName;
        row.Message = "Uninstalling " + row.Spec.PackageName;
        statusText = "Uninstalling " + row.Spec.DisplayName;
        EditorUtility.DisplayProgressBar("VLiveKitPackageManager", statusText, 0.5f);
        removeRequest = Client.Remove(row.Spec.PackageName);
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
        if (stylesReady &&
            headerStyle != null &&
            cardStyle != null &&
            badgeStyle != null &&
            mutedStyle != null &&
            toolbarButtonStyle != null &&
            primaryButtonStyle != null &&
            metricValueStyle != null &&
            positiveNoticeStyle != null &&
            rowTitleStyle != null &&
            tableHeaderStyle != null)
        {
            return;
        }

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 20,
            normal = { textColor = PrimaryTextColor() }
        };
        cardStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(12, 12, 8, 8),
            margin = new RectOffset(8, 8, 4, 4),
            normal =
            {
                background = MakeSolidTexture(PanelColor())
            }
        };
        badgeStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            padding = new RectOffset(8, 8, 2, 2),
            normal = { textColor = PrimaryTextColor() }
        };
        mutedStyle = new GUIStyle(EditorStyles.label)
        {
            wordWrap = false,
            normal = { textColor = SecondaryTextColor() }
        };
        toolbarButtonStyle = new GUIStyle(EditorStyles.miniButton)
        {
            fixedHeight = 26,
            fontSize = 11
        };
        primaryButtonStyle = new GUIStyle(EditorStyles.miniButton)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fixedHeight = 28,
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.88f, 0.94f, 1f) : new Color(0.05f, 0.24f, 0.52f) }
        };
        metricValueStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = PrimaryTextColor() }
        };
        positiveNoticeStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
        {
            padding = new RectOffset(12, 12, 7, 7),
            normal = { textColor = PrimaryTextColor() }
        };
        rowTitleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = PrimaryTextColor() }
        };
        tableHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = SecondaryTextColor() }
        };
        stylesReady = true;
    }

    private void DrawHeader()
    {
        var rect = GUILayoutUtility.GetRect(0f, 84f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, SurfaceColor());
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - 1f, rect.width, 1f), SeparatorColor());

        const float buttonGap = 8f;
        const float buttonY = 14f;
        const float shortButtonWidth = 108f;
        const float installAllButtonWidth = 96f;
        const float installMissingButtonWidth = 118f;

        var refreshRect = new Rect(rect.xMax - 18f - shortButtonWidth, rect.y + buttonY, shortButtonWidth, 28f);
        var updateAllRect = new Rect(refreshRect.x - buttonGap - shortButtonWidth, rect.y + buttonY, shortButtonWidth, 28f);
        var installMissingRect = new Rect(updateAllRect.x - buttonGap - installMissingButtonWidth, rect.y + buttonY, installMissingButtonWidth, 28f);
        var installAllRect = new Rect(installMissingRect.x - buttonGap - installAllButtonWidth, rect.y + buttonY, installAllButtonWidth, 28f);
        var titleWidth = Mathf.Max(220f, installAllRect.x - rect.x - 30f);

        var titleRect = new Rect(rect.x + 18f, rect.y + 13f, titleWidth, 24f);
        GUI.Label(titleRect, "VLiveKit Package Manager", headerStyle);

        var subtitleRect = new Rect(rect.x + 18f, rect.y + 41f, titleWidth, 18f);
        GUI.Label(subtitleRect, "Choose what to add. Local packages stay untouched.", mutedStyle);

        GUI.enabled = !isChecking && addRequest == null && removeRequest == null;
        if (GUI.Button(installAllRect, "Install All", primaryButtonStyle))
        {
            QueueRows(IsInstallAllCandidate);
        }

        if (GUI.Button(installMissingRect, "Install Missing", primaryButtonStyle))
        {
            QueueRows(row => row.State == InstallState.Missing);
        }

        if (GUI.Button(updateAllRect, "Update", primaryButtonStyle))
        {
            QueueRows(row => row.CanUpdate);
        }

        if (GUI.Button(refreshRect, "Refresh", primaryButtonStyle))
        {
            Refresh();
        }

        GUI.enabled = true;

        var statusRect = new Rect(rect.xMax - 424f, rect.y + 51f, 406f, 18f);
        GUI.Label(statusRect, catalogSource + " - " + statusText, new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleRight });
    }

    private static Color AccentColor(float alpha)
    {
        return new Color(0.0f, 0.48f, 1f, alpha);
    }

    private static Color SurfaceColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(0.118f, 0.118f, 0.118f) : new Color(0.965f, 0.965f, 0.965f);
    }

    private static Color PanelColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(0.158f, 0.158f, 0.158f) : new Color(0.992f, 0.992f, 0.992f);
    }

    private static Color SubtlePanelColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(0.185f, 0.185f, 0.185f) : new Color(0.935f, 0.935f, 0.935f);
    }

    private static Color SeparatorColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.09f) : new Color(0f, 0f, 0f, 0.11f);
    }

    private static Color PrimaryTextColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.12f, 0.12f, 0.12f);
    }

    private static Color SecondaryTextColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.42f, 0.42f, 0.42f);
    }

    private static Texture2D MakeSolidTexture(Color color)
    {
        var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private static void DrawSeparator(float topPadding = 4f, float bottomPadding = 4f)
    {
        GUILayout.Space(topPadding);
        var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, SeparatorColor());
        GUILayout.Space(bottomPadding);
    }

    private void DrawOperationProgress()
    {
        if (!IsOperating)
        {
            return;
        }

        GUILayout.Space(6f);
        var rect = GUILayoutUtility.GetRect(1f, 4f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, SeparatorColor());
        var fillRect = new Rect(rect.x, rect.y, rect.width * GetOperationProgress(), rect.height);
        EditorGUI.DrawRect(fillRect, AccentColor(1f));
        GUILayout.Space(8f);
    }

    private void DrawBackupNotice()
    {
        if (ProjectHasGitMetadata())
        {
            return;
        }

        DrawPositiveNotice("If this project is not tracked with Git, make a project backup before installing or updating packages.");
    }

    private void DrawPositiveNotice(string message)
    {
        var rect = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, SubtlePanelColor());
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), SeparatorColor());
        GUI.Label(rect, message, positiveNoticeStyle);
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

        EditorGUILayout.BeginHorizontal(GUILayout.Height(30f));
        GUILayout.Space(12f);
        GUILayout.Label("Installed " + installed, metricValueStyle, GUILayout.Width(118f));
        GUILayout.Label("Updates " + updates, metricValueStyle, GUILayout.Width(118f));
        GUILayout.Label("Missing " + missing, metricValueStyle, GUILayout.Width(118f));
        GUILayout.Label("Local " + local, metricValueStyle, GUILayout.Width(118f));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        DrawSeparator(2f, 6f);
    }

    private void DrawMetric(string label, string value, float accentAlpha)
    {
        EditorGUILayout.BeginVertical(cardStyle, GUILayout.Height(50f));
        GUILayout.Space(2f);
        var valueStyle = new GUIStyle(metricValueStyle);
        if (accentAlpha > 0.75f)
        {
            valueStyle.normal.textColor = PrimaryTextColor();
        }

        GUILayout.Label(value, valueStyle);
        GUILayout.Label(label, new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleCenter });
        EditorGUILayout.EndVertical();
    }

    private void DrawPackageList()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        DrawTableHeader();
        foreach (var row in rows)
        {
            DrawPackageRow(row);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawTableHeader()
    {
        var rect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, SurfaceColor());
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), SeparatorColor());

        var actionsX = rect.xMax - 378f;
        GUI.Label(new Rect(rect.x + 16f, rect.y + 7f, 90f, 16f), "Status", tableHeaderStyle);
        GUI.Label(new Rect(rect.x + 112f, rect.y + 7f, actionsX - rect.x - 292f, 16f), "Package", tableHeaderStyle);
        GUI.Label(new Rect(actionsX - 178f, rect.y + 7f, 80f, 16f), "Installed", tableHeaderStyle);
        GUI.Label(new Rect(actionsX - 88f, rect.y + 7f, 74f, 16f), "Latest", tableHeaderStyle);
        GUI.Label(new Rect(actionsX, rect.y + 7f, 360f, 16f), "Actions", tableHeaderStyle);
    }

    private void DrawPackageRow(PackageRow row)
    {
        var hasMessage = !string.IsNullOrEmpty(row.Message);
        var rect = GUILayoutUtility.GetRect(0f, hasMessage ? 76f : 58f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, PanelColor());
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), SeparatorColor());

        var actionsX = rect.xMax - 378f;
        DrawStatusBadge(new Rect(rect.x + 16f, rect.y + 12f, 88f, 22f), row);

        var packageWidth = Mathf.Max(180f, actionsX - rect.x - 296f);
        GUI.Label(new Rect(rect.x + 112f, rect.y + 9f, packageWidth, 18f), row.Spec.DisplayName, rowTitleStyle);
        GUI.Label(new Rect(rect.x + 112f, rect.y + 30f, packageWidth, 18f), row.Spec.PackageName, mutedStyle);

        GUI.Label(new Rect(actionsX - 178f, rect.y + 13f, 80f, 18f), string.IsNullOrEmpty(row.InstalledVersion) ? "-" : row.InstalledVersion, EditorStyles.label);
        GUI.Label(new Rect(actionsX - 88f, rect.y + 13f, 74f, 18f), row.LatestLabel, EditorStyles.label);

        if (DrawActionButton(new Rect(actionsX, rect.y + 11f, 62f, 24f), "GitHub", true))
        {
            Application.OpenURL(row.Spec.RepositoryUrl);
        }

        if (DrawActionButton(new Rect(actionsX + 66f, rect.y + 11f, 54f, 24f), "Docs", true))
        {
            Application.OpenURL(row.Spec.DocumentationUrl);
        }

        var canImportSamples = row.Spec.PackageName != CatalogPackageName;
        if (DrawActionButton(new Rect(actionsX + 124f, rect.y + 11f, 72f, 24f), "Samples", addRequest == null && removeRequest == null && !isChecking && row.State != InstallState.Missing && canImportSamples))
        {
            ImportSamples(row);
        }

        var canInstall = addRequest == null && removeRequest == null && !isChecking && row.CanInstallFromRegistry;
        if (row.State == InstallState.Missing)
        {
            if (DrawActionButton(new Rect(actionsX + 200f, rect.y + 11f, 76f, 24f), "Install", canInstall))
            {
                QueueRows(candidate => candidate == row);
            }
        }
        else if (row.CanUpdate)
        {
            if (DrawActionButton(new Rect(actionsX + 200f, rect.y + 11f, 76f, 24f), "Update", canInstall))
            {
                QueueRows(candidate => candidate == row);
            }
        }
        else
        {
            DrawActionButton(new Rect(actionsX + 200f, rect.y + 11f, 76f, 24f), row.IsLocal ? "Local" : "Current", false);
        }

        if (DrawActionButton(new Rect(actionsX + 280f, rect.y + 11f, 78f, 24f), "Uninstall", addRequest == null && removeRequest == null && !isChecking && row.CanUninstall))
        {
            UninstallRow(row);
        }

        if (hasMessage)
        {
            GUI.Label(new Rect(rect.x + 112f, rect.y + 51f, rect.width - 136f, 18f), row.Message, mutedStyle);
        }
    }

    private bool DrawActionButton(Rect rect, string label, bool enabled)
    {
        var wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && enabled;
        var clicked = GUI.Button(rect, label, toolbarButtonStyle);
        GUI.enabled = wasEnabled;
        return clicked;
    }

    private void DrawStatusBadge(Rect rect, PackageRow row)
    {
        var label = row.StatusLabel;
        EditorGUI.DrawRect(rect, GetRowAccentColor(row));
        GUI.Label(rect, label, badgeStyle);
    }

    private static Color GetRowAccentColor(PackageRow row)
    {
        if (row.CanUpdate)
        {
            return AccentColor(EditorGUIUtility.isProSkin ? 0.22f : 0.12f);
        }

        if (row.State == InstallState.Missing)
        {
            return SubtlePanelColor();
        }

        if (row.IsLocal)
        {
            return SubtlePanelColor();
        }

        return SubtlePanelColor();
    }

    private void DrawVersionBlock(string label, string value)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(90f));
        GUILayout.Label(label, mutedStyle);
        GUILayout.Label(value, EditorStyles.boldLabel);
        EditorGUILayout.EndVertical();
    }

    private static void ImportSamples(PackageRow row)
    {
        if (row == null || row.State == InstallState.Missing)
        {
            return;
        }

        var version = ResolveSamplePackageVersion(row);

        if (string.IsNullOrEmpty(version))
        {
            if (ImportLocalSamples(row, "local"))
            {
                return;
            }

            ShowSoftNotice("Samples are not ready yet for " + row.Spec.DisplayName + ". Refresh after Unity finishes resolving packages.");
            return;
        }

        var samples = new List<Sample>(Sample.FindByPackage(row.Spec.PackageName, version));
        if (samples.Count == 0)
        {
            if (ImportLocalSamples(row, version))
            {
                return;
            }

            ShowSoftNotice("No Samples~ or Sample folder was found for " + row.Spec.DisplayName + ".");
            return;
        }

        var imported = 0;
        foreach (var sample in samples)
        {
            if (sample.Import(Sample.ImportOptions.OverridePreviousImports))
            {
                imported++;
            }
        }

        AssetDatabase.Refresh();
        ShowSoftNotice("Imported " + imported + " sample(s) for " + row.Spec.DisplayName + ".");
    }

    private static bool ImportLocalSamples(PackageRow row, string version)
    {
        var packageJsonPath = FindInstalledPackageJson(row.Spec);
        if (string.IsNullOrEmpty(packageJsonPath))
        {
            return false;
        }

        var packageRoot = Path.GetDirectoryName(packageJsonPath);
        if (string.IsNullOrEmpty(packageRoot))
        {
            return false;
        }

        var imported = 0;
        var samplesRoot = Path.Combine(packageRoot, "Samples~");
        if (Directory.Exists(samplesRoot))
        {
            imported += ImportSamplesRoot(row.Spec.DisplayName, version, samplesRoot);
        }

        var visibleSampleRoot = Path.Combine(packageRoot, "Sample");
        if (Directory.Exists(visibleSampleRoot))
        {
            imported += CopySampleFolder(row.Spec.DisplayName, version, visibleSampleRoot, "Sample");
        }

        if (imported == 0)
        {
            return false;
        }

        AssetDatabase.Refresh();
        ShowSoftNotice("Imported " + imported + " local sample folder(s) for " + row.Spec.DisplayName + ".");
        return true;
    }

    private static string ResolveSamplePackageVersion(PackageRow row)
    {
        var version = row.InstalledVersion;
        if (!IsPlaceholderVersion(version))
        {
            return version;
        }

        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(row.Spec.PackageName);
        if (packageInfo != null)
        {
            if (!IsPlaceholderVersion(packageInfo.version))
            {
                return packageInfo.version;
            }

            var packageJsonPath = Path.Combine(packageInfo.resolvedPath, "package.json");
            version = ReadPackageJsonVersion(packageJsonPath);
            if (!IsPlaceholderVersion(version))
            {
                return version;
            }
        }

        version = ReadLocalPackageVersion(row.Spec);
        return IsPlaceholderVersion(version) ? null : version;
    }

    private static bool IsPlaceholderVersion(string version)
    {
        return string.IsNullOrEmpty(version) ||
            version == "manifest" ||
            version == "local" ||
            version == "assets";
    }

    private static int ImportSamplesRoot(string displayName, string version, string sampleRoot)
    {
        var folders = Directory.GetDirectories(sampleRoot);
        if (folders.Length == 0)
        {
            return CopySampleFolder(displayName, version, sampleRoot, Path.GetFileName(sampleRoot));
        }

        var imported = 0;
        foreach (var folder in folders)
        {
            imported += CopySampleFolder(displayName, version, folder, Path.GetFileName(folder));
        }

        return imported;
    }

    private static int CopySampleFolder(string displayName, string version, string sourceFolder, string sampleName)
    {
        if (string.IsNullOrEmpty(sampleName))
        {
            sampleName = "Sample";
        }

        var destination = ToProjectPath("Assets/Samples/" + SanitizePathPart(displayName) + "/" + version + "/" + SanitizePathPart(sampleName));
        if (Directory.Exists(destination))
        {
            var overwrite = EditorUtility.DisplayDialog(
                "VLiveKit Samples",
                "Replace existing sample?\n\n" + destination,
                "Replace",
                "Skip");
            if (!overwrite)
            {
                return 0;
            }

            FileUtil.DeleteFileOrDirectory(destination);
            FileUtil.DeleteFileOrDirectory(destination + ".meta");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        FileUtil.CopyFileOrDirectory(sourceFolder, destination);
        return 1;
    }

    private static string SanitizePathPart(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "Sample";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '_');
        }

        value = value.Trim();
        return string.IsNullOrEmpty(value) ? "Sample" : value;
    }

    private static void OpenRecommendedHDRPVolumeSettingsWindow()
    {
        var window = GetWindow<RecommendedHDRPVolumeSettingsWindow>(true, "VLiveKit Recommended Settings");
        window.minSize = new Vector2(420f, 500f);
        window.ShowUtility();
    }

    private static void ApplyRecommendedHDRPVolumeSettings(HashSet<string> selectedTypeNames)
    {
        if (!IsLensFiltersInstalledInProject())
        {
            ShowSoftNotice("VLive Lens Filters is not installed. Install it first, then apply the recommended HDRP Volume settings.");
            return;
        }

        var settingsAsset = GetHDRPGlobalSettingsAsset();
        if (settingsAsset == null)
        {
            ShowSoftNotice("HDRP Global Settings asset was not found. Open Project Settings > Graphics once, then try again.");
            return;
        }

        var missingTypes = new List<string>();
        var beforeTypes = ResolveRecommendedPostProcessTypes(RecommendedBeforePostProcesses, missingTypes, selectedTypeNames);
        var afterTypes = ResolveRecommendedPostProcessTypes(RecommendedAfterPostProcesses, missingTypes, selectedTypeNames);

        if (beforeTypes.Count == 0 && afterTypes.Count == 0)
        {
            ShowSoftNotice("Recommended post process types were not found. Install VLive Lens Filters, then try again.");
            return;
        }

        var serializedObject = new SerializedObject(settingsAsset);
        serializedObject.Update();

        var changed = false;
        changed |= ApplyRecommendedPostProcessOrder(serializedObject, HDRPBeforePostProcessPropertyName, beforeTypes);
        changed |= ApplyRecommendedPostProcessOrder(serializedObject, HDRPAfterPostProcessPropertyName, afterTypes);

        if (changed)
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(settingsAsset);
            AssetDatabase.SaveAssets();
        }

        var message = changed
            ? "Applied recommended HDRP custom post process order."
            : "Recommended HDRP custom post process order is already applied.";

        if (missingTypes.Count > 0)
        {
            message += "\n\nSkipped missing type(s):\n" + string.Join("\n", missingTypes);
        }

        ShowSoftNotice(message);
    }

    private bool IsLensFiltersInstalled()
    {
        var row = FindRowByPackageId(LensFiltersPackageName);
        return row != null && row.State != InstallState.Missing;
    }

    private static bool IsLensFiltersInstalledInProject()
    {
        var manifestJson = ReadManifestJson();
        if (!string.IsNullOrEmpty(manifestJson) && manifestJson.Contains("\"" + LensFiltersPackageName + "\""))
        {
            return true;
        }

        return Directory.Exists(ToProjectPath("Packages/VLiveKit_LiveLensFilters")) ||
            AssetDatabase.IsValidFolder("Assets/toshi.VLiveKit/LiveLensFilters") ||
            Directory.Exists(ToProjectPath("Assets/toshi.VLiveKit/LiveLensFilters"));
    }

    private static UnityEngine.Object GetHDRPGlobalSettingsAsset()
    {
        var settingsType = Type.GetType(HDRenderPipelineGlobalSettingsTypeName);
        if (settingsType == null)
        {
            return null;
        }

        var ensureMethod = settingsType.GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
        if (ensureMethod != null)
        {
            try
            {
                return ensureMethod.Invoke(null, new object[] { true }) as UnityEngine.Object;
            }
            catch (TargetInvocationException exception)
            {
                Debug.Log("VLiveKitPackageManager could not ensure HDRP Global Settings asset. " + exception.InnerException?.Message);
                return null;
            }
        }

        var instanceProperty = settingsType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
        return instanceProperty != null ? instanceProperty.GetValue(null) as UnityEngine.Object : null;
    }

    private static List<string> ResolveRecommendedPostProcessTypes(RecommendedPostProcessType[] recommendedTypes, List<string> missingTypes, HashSet<string> selectedTypeNames)
    {
        var resolvedTypes = new List<string>();
        foreach (var recommendedType in recommendedTypes)
        {
            if (selectedTypeNames != null && !selectedTypeNames.Contains(recommendedType.TypeName))
            {
                continue;
            }

            var type = Type.GetType(recommendedType.TypeName + ", " + recommendedType.AssemblyName);
            if (type == null)
            {
                missingTypes.Add(recommendedType.TypeName);
                continue;
            }

            resolvedTypes.Add(type.AssemblyQualifiedName);
        }

        return resolvedTypes;
    }

    private static bool ApplyRecommendedPostProcessOrder(SerializedObject serializedObject, string propertyName, List<string> recommendedTypes)
    {
        var property = serializedObject.FindProperty(propertyName);
        if (property == null || !property.isArray)
        {
            return false;
        }

        var currentTypes = new List<string>();
        for (var i = 0; i < property.arraySize; i++)
        {
            var value = property.GetArrayElementAtIndex(i).stringValue;
            if (!string.IsNullOrEmpty(value))
            {
                currentTypes.Add(value);
            }
        }

        var orderedTypes = new List<string>(recommendedTypes);
        foreach (var currentType in currentTypes)
        {
            if (!orderedTypes.Contains(currentType))
            {
                orderedTypes.Add(currentType);
            }
        }

        if (ListsMatch(currentTypes, orderedTypes))
        {
            return false;
        }

        property.arraySize = orderedTypes.Count;
        for (var i = 0; i < orderedTypes.Count; i++)
        {
            property.GetArrayElementAtIndex(i).stringValue = orderedTypes[i];
        }

        return true;
    }

    private static bool ListsMatch(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
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

    private bool IsOperating => addRequest != null || removeRequest != null || pendingOperations.Count > 0;

    private float GetOperationProgress()
    {
        if (removeRequest != null)
        {
            return 0.5f;
        }

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
            EnsureInstallerRow();
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
            var packageSpec = new PackageSpec(
                item.name,
                item.displayName,
                item.packageFolderPath,
                item.assetFolderPath,
                repositoryUrl,
                documentationUrl);
            if (IsBlockedInstallTarget(packageSpec))
            {
                continue;
            }

            loadedRows.Add(new PackageRow(packageSpec));
        }

        return loadedRows.Count > 0;
    }

    private void LoadFallbackCatalog()
    {
        rows.Clear();
        rows.Add(new PackageRow(InstallerPackageSpec));
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

    private static bool EnsureExternalScopedRegistry()
    {
        return VLiveKitManifestUtility.EnsureExternalScopedRegistry();
    }

    private static bool IsBlockedInstallTarget(PackageSpec package)
    {
        return ContainsIgnoreCase(package.PackageName, ThirdPartyUtilitiesPackageName) ||
            ContainsIgnoreCase(package.PackageName, "thirdpartyassets") ||
            ContainsIgnoreCase(package.DisplayName, "thirdpartyassets") ||
            ContainsIgnoreCase(package.PackageFolderPath, ThirdPartyAssetsRepositoryName) ||
            ContainsIgnoreCase(package.AssetFolderPath, ThirdPartyAssetsRepositoryName) ||
            ContainsIgnoreCase(package.RepositoryUrl, ThirdPartyAssetsRepositoryName) ||
            ContainsIgnoreCase(package.DocumentationUrl, ThirdPartyAssetsRepositoryName);
    }

    private static bool ContainsIgnoreCase(string value, string pattern)
    {
        return !string.IsNullOrEmpty(value) &&
            !string.IsNullOrEmpty(pattern) &&
            value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ProjectHasGitMetadata()
    {
        var gitPath = ToProjectPath(".git");
        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }

    private static bool IsInstallerSourcePresentInProject()
    {
        var guids = AssetDatabase.FindAssets("VLiveKitInstaller t:Script");
        foreach (var guid in guids)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (assetPath.EndsWith("/VLiveKitInstaller.cs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return File.Exists(ToProjectPath("Packages/VLiveKit/Editor/VLiveKitInstaller.cs")) ||
            File.Exists(ToProjectPath("Assets/VLiveKit/Editor/VLiveKitInstaller.cs"));
    }

    private static bool IsSubmodulePackage(PackageSpec package)
    {
        var gitPath = Path.Combine(ToProjectPath(package.PackageFolderPath), ".git");
        return File.Exists(gitPath) || Directory.Exists(gitPath);
    }

    private static string ReadInstallerPackageVersion()
    {
        var candidates = new[]
        {
            ToProjectPath("Packages/VLiveKit/package.json"),
            ToProjectPath("Assets/VLiveKit/package.json")
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var json = File.ReadAllText(candidate);
            var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static string ReadLocalPackageVersion(PackageSpec package)
    {
        var packageJsonPath = FindLocalPackageJson(package);
        return ReadPackageJsonVersion(packageJsonPath);
    }

    private static string ReadResolvedPackageVersion(UnityEditor.PackageManager.PackageInfo packageInfo)
    {
        if (packageInfo == null || string.IsNullOrEmpty(packageInfo.resolvedPath))
        {
            return null;
        }

        return ReadPackageJsonVersion(Path.Combine(packageInfo.resolvedPath, "package.json"));
    }

    private static string ReadPackageJsonVersion(string packageJsonPath)
    {
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

    private static string FindInstalledPackageJson(PackageSpec package)
    {
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(package.PackageName);
        if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
        {
            var packageJsonPath = Path.Combine(packageInfo.resolvedPath, "package.json");
            if (File.Exists(packageJsonPath))
            {
                return packageJsonPath;
            }
        }

        return FindLocalPackageJson(package);
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

    private static PackageSpec InstallerPackageSpec
    {
        get
        {
            return new PackageSpec(
                CatalogPackageName,
                "VLiveKit Package Manager",
                "Packages/VLiveKit",
                "Packages/VLiveKit",
                "https://github.com/toshi-kundesu/VLiveKit",
                "https://github.com/toshi-kundesu/VLiveKit#readme");
        }
    }

    private void EnsureInstallerRow()
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Spec.PackageName == CatalogPackageName)
            {
                if (i > 0)
                {
                    var row = rows[i];
                    rows.RemoveAt(i);
                    rows.Insert(0, row);
                }

                return;
            }
        }

        rows.Insert(0, new PackageRow(InstallerPackageSpec));
    }

    private sealed class RecommendedHDRPVolumeSettingsWindow : EditorWindow
    {
        private readonly HashSet<string> selectedTypeNames = new HashSet<string>();
        private Vector2 scrollPosition;

        private void OnEnable()
        {
            SelectAll();
        }

        private void OnGUI()
        {
            GUILayout.Label("Recommended Settings", EditorStyles.boldLabel);
            GUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(70f)))
            {
                SelectAll();
            }

            if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(70f)))
            {
                selectedTypeNames.Clear();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawRecommendedGroup("Before Post Process", RecommendedBeforePostProcesses);
            DrawRecommendedGroup("After Post Process", RecommendedAfterPostProcesses);
            EditorGUILayout.EndScrollView();

            GUILayout.Space(8f);
            GUI.enabled = selectedTypeNames.Count > 0;
            if (GUILayout.Button("Apply Checked Items", GUILayout.Height(30f)))
            {
                ApplyRecommendedHDRPVolumeSettings(new HashSet<string>(selectedTypeNames));
            }

            GUI.enabled = true;
        }

        private void DrawRecommendedGroup(string label, RecommendedPostProcessType[] postProcessTypes)
        {
            GUILayout.Space(8f);
            GUILayout.Label(label, EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            foreach (var postProcessType in postProcessTypes)
            {
                var selected = selectedTypeNames.Contains(postProcessType.TypeName);
                var nextSelected = EditorGUILayout.ToggleLeft(GetRecommendedDisplayName(postProcessType), selected);
                if (nextSelected == selected)
                {
                    continue;
                }

                if (nextSelected)
                {
                    selectedTypeNames.Add(postProcessType.TypeName);
                }
                else
                {
                    selectedTypeNames.Remove(postProcessType.TypeName);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void SelectAll()
        {
            selectedTypeNames.Clear();
            AddAll(RecommendedBeforePostProcesses);
            AddAll(RecommendedAfterPostProcesses);
        }

        private void AddAll(RecommendedPostProcessType[] postProcessTypes)
        {
            foreach (var postProcessType in postProcessTypes)
            {
                selectedTypeNames.Add(postProcessType.TypeName);
            }
        }

        private static string GetRecommendedDisplayName(RecommendedPostProcessType postProcessType)
        {
            var dotIndex = postProcessType.TypeName.LastIndexOf('.');
            return dotIndex >= 0 ? postProcessType.TypeName.Substring(dotIndex + 1) : postProcessType.TypeName;
        }
    }

    private sealed class FirstRunPromptWindow : EditorWindow
    {
        private string prefsKey;
        private bool showBackupTip;
        private Action openAction;

        public static void Open(string prefsKey, bool showBackupTip, Action openAction)
        {
            var window = GetWindow<FirstRunPromptWindow>(true, "VLiveKit");
            window.prefsKey = prefsKey;
            window.showBackupTip = showBackupTip;
            window.openAction = openAction;
            window.minSize = new Vector2(430f, 170f);
            window.maxSize = new Vector2(430f, 170f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            GUILayout.Space(12f);
            GUILayout.Label("VLiveKit Package Manager is ready", EditorStyles.boldLabel);
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("Open it when you want to choose packages, import samples, or check updates.", EditorStyles.wordWrappedLabel);

            if (showBackupTip)
            {
                GUILayout.Space(8f);
                var rect = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.185f, 0.185f, 0.185f) : new Color(0.935f, 0.935f, 0.935f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.09f) : new Color(0f, 0f, 0f, 0.11f));
                GUI.Label(
                    rect,
                    "Good to go. Keeping a project backup is a nice safety net if this project is not tracked with Git.",
                    new GUIStyle(EditorStyles.wordWrappedLabel)
                    {
                        padding = new RectOffset(10, 10, 7, 7),
                        normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.12f, 0.12f, 0.12f) }
                    });
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open", GUILayout.Height(28f)))
            {
                EditorPrefs.SetBool(prefsKey, true);
                Close();
                openAction?.Invoke();
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

    private readonly struct RecommendedPostProcessType
    {
        public RecommendedPostProcessType(string typeName, string assemblyName)
        {
            TypeName = typeName;
            AssemblyName = assemblyName;
        }

        public string TypeName { get; }
        public string AssemblyName { get; }
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
        public bool CanInstallFromRegistry => !IsBlockedInstallTarget(Spec) && !IsLocal && LatestCheckState != LatestState.Checking;
        public bool CanUpdate => State != InstallState.Missing && !IsLocal && !string.IsNullOrEmpty(InstalledVersion) && !string.IsNullOrEmpty(LatestVersion) && CompareVersions(InstalledVersion, LatestVersion) < 0;
        public bool CanUninstall => State == InstallState.PackageManager && Spec.PackageName != CatalogPackageName;

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
