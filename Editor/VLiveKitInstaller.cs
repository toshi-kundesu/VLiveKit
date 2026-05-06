#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

[InitializeOnLoad]
internal static class VLiveKitInstaller
{
    private const string MenuRoot = "toshi/VLiveKit/";
    private static string PromptEditorPrefsKey => "VLiveKitInstaller.PromptShown." + Application.dataPath;

    private static readonly PackageSpec[] Packages =
    {
        new("com.toshi.vlivekit.artnetlink", "VLiveKit ArtNetLink", "https://github.com/toshi-kundesu/VLiveKit_ArtNetLink.git?path=/Assets/toshi.VLiveKit/ArtNetLink", "Packages/VLiveKit_ArtNetLink", "Assets/toshi.VLiveKit/ArtNetLink"),
        new("com.toshi.vlivekit.cameraunit", "VLive Camera Unit", "https://github.com/toshi-kundesu/VLiveKit_camera.git?path=/Assets/toshi.VLiveKit/VLiveCameraUnit", "Packages/VLiveKit_camera", "Assets/toshi.VLiveKit/VLiveCameraUnit"),
        new("com.toshi.vlivekit.ledvision", "VLiveKit LED Vision", "https://github.com/toshi-kundesu/VLiveKit_LEDVision.git?path=/Assets/toshi.VLiveKit/LEDVision", "Packages/VLiveKit_LEDVision", "Assets/toshi.VLiveKit/LEDVision"),
        new("com.toshi.vlivekit.lensfilters", "VLive Lens Filters", "https://github.com/toshi-kundesu/VLiveKit_LiveLensFilters.git?path=/Assets/toshi.VLiveKit/LiveLensFilters", "Packages/VLiveKit_LiveLensFilters", "Assets/toshi.VLiveKit/LiveLensFilters"),
        new("com.toshi.vlivekit.livetoon", "VLive Live Toon", "https://github.com/toshi-kundesu/VLiveKit_LiveToon.git?path=/Assets/toshi.VLiveKit/livetoon", "Packages/VLiveKit_LiveToon", "Assets/toshi.VLiveKit/livetoon"),
        new("com.toshi.vlivekit.performeract", "VLive Performer Act", "https://github.com/toshi-kundesu/VLiveKit_PerformerAct.git?path=/Assets/toshi.VLiveKit/PerformerAct", "Packages/VLiveKit_PerformerAct", "Assets/toshi.VLiveKit/PerformerAct"),
        new("com.toshi.vlivekit.testassetscontainer", "VLiveKit Test Assets Container", "https://github.com/toshi-kundesu/VLiveKit_TestAssetsContainer.git?path=/Assets/toshi.VLiveKit/TestAssetsContainer", "Packages/VLiveKit_TestAssetsContainer", "Assets/toshi.VLiveKit/TestAssetsContainer"),
        new("com.toshi.vlivekit.stagebuilder", "VLiveKit StageBuilder", "https://github.com/toshi-kundesu/VLiveKit_StageBuilder.git?path=/Assets/toshi.VLiveKit/StageBuilder", "Packages/VLiveKit_StageBuilder", "Assets/toshi.VLiveKit/StageBuilder"),
        new("com.toshi.vlivekit.stageeffect", "VLiveKit StageEffect", "https://github.com/toshi-kundesu/VLiveKit_StageEffect.git?path=/Assets/toshi.VLiveKit/StageEffect", "Packages/VLiveKit_StageEffect", "Assets/toshi.VLiveKit/StageEffect"),
        new("com.toshi.vlivekit.thirdpartyutilities", "VLive Third Party Utilities", "https://github.com/toshi-kundesu/VLiveKit_ThirdPartyUtilities.git?path=/Assets/toshi.VLiveKit/ThirdPartyUtilities", "Packages/VLiveKit_ThirdPartyUtilities", "Assets/toshi.VLiveKit/ThirdPartyUtilities"),
        new("com.toshi.vlivekit.videorack", "VLiveKit VideoRack", "https://github.com/toshi-kundesu/VLiveKit_VideoRack.git?path=/Assets/toshi.VLiveKit/VideoRack", "Packages/VLiveKit_VideoRack", "Assets/toshi.VLiveKit/VideoRack"),
    };

    private static readonly Queue<PackageSpec> PendingPackages = new();
    private static readonly List<PackageStatus> LastStatuses = new();
    private static AddRequest currentRequest;
    private static ListRequest listRequest;
    private static PackageSpec currentPackage;
    private static StatusCheckMode statusCheckMode;
    private static int totalCount;
    private static int installedCount;

    static VLiveKitInstaller()
    {
        EditorApplication.delayCall += ShowInstallPromptOnce;
    }

    [MenuItem(MenuRoot + "Install Missing Packages")]
    private static void InstallMissingPackages()
    {
        StartStatusCheck(StatusCheckMode.InstallMissing);
    }

    [MenuItem(MenuRoot + "Check Install Status")]
    private static void CheckInstallStatus()
    {
        StartStatusCheck(StatusCheckMode.ShowStatusOnly);
    }

    [MenuItem(MenuRoot + "Open Installer Dialog")]
    private static void OpenInstallerDialog()
    {
        StartStatusCheck(StatusCheckMode.ShowPrompt);
    }

    private static void ShowInstallPromptOnce()
    {
        if (EditorPrefs.GetBool(PromptEditorPrefsKey, false))
        {
            return;
        }

        EditorPrefs.SetBool(PromptEditorPrefsKey, true);
        StartStatusCheck(StatusCheckMode.ShowPrompt);
    }

    private static void StartStatusCheck(StatusCheckMode mode)
    {
        if (currentRequest != null)
        {
            Debug.LogWarning("VLiveKit package installation is already running.");
            return;
        }

        if (listRequest != null)
        {
            Debug.LogWarning("VLiveKit package status check is already running.");
            return;
        }

        statusCheckMode = mode;
        EditorUtility.DisplayProgressBar("VLiveKit Installer", "Checking installed packages...", 0f);
        listRequest = Client.List(true, false);
        EditorApplication.update += UpdateStatusCheck;
    }

    private static void UpdateStatusCheck()
    {
        if (listRequest == null || !listRequest.IsCompleted)
        {
            return;
        }

        EditorApplication.update -= UpdateStatusCheck;
        EditorUtility.ClearProgressBar();

        if (listRequest.Status == StatusCode.Success)
        {
            RefreshStatuses(listRequest.Result);
        }
        else
        {
            var errorMessage = listRequest.Error != null ? listRequest.Error.message : "Unknown Package Manager error.";
            Debug.LogWarning($"Package Manager status check failed. Falling back to manifest and asset checks. {errorMessage}");
            RefreshStatuses(null);
        }

        listRequest = null;

        switch (statusCheckMode)
        {
            case StatusCheckMode.InstallMissing:
                QueueMissingPackages();
                break;
            case StatusCheckMode.ShowStatusOnly:
                ShowStatusDialog();
                break;
            default:
                ShowInstallPrompt();
                break;
        }
    }

    private static void RefreshStatuses(PackageCollection packageCollection)
    {
        LastStatuses.Clear();

        var resolvedPackageNames = new HashSet<string>();
        if (packageCollection != null)
        {
            foreach (var packageInfo in packageCollection)
            {
                resolvedPackageNames.Add(packageInfo.name);
            }
        }

        var manifestJson = ReadManifestJson();
        foreach (var package in Packages)
        {
            LastStatuses.Add(GetPackageStatus(package, resolvedPackageNames, manifestJson));
        }
    }

    private static PackageStatus GetPackageStatus(PackageSpec package, HashSet<string> resolvedPackageNames, string manifestJson)
    {
        if (resolvedPackageNames.Contains(package.PackageName))
        {
            return new PackageStatus(package, InstallState.PackageManager);
        }

        if (!string.IsNullOrEmpty(manifestJson) && manifestJson.Contains("\"" + package.PackageName + "\""))
        {
            return new PackageStatus(package, InstallState.Manifest);
        }

        if (Directory.Exists(ToProjectPath(package.PackageFolderPath)))
        {
            return new PackageStatus(package, InstallState.PackageFolderOrSubmodule);
        }

        if (AssetDatabase.IsValidFolder(package.AssetFolderPath) || Directory.Exists(ToProjectPath(package.AssetFolderPath)))
        {
            return new PackageStatus(package, InstallState.AssetsFolder);
        }

        return new PackageStatus(package, InstallState.Missing);
    }

    private static void ShowInstallPrompt()
    {
        var missingCount = GetMissingCount();
        var summary = BuildStatusSummary();

        if (missingCount == 0)
        {
            EditorUtility.DisplayDialog("VLiveKit Installer", "All VLiveKit packages are already present.\n\n" + summary, "OK");
            return;
        }

        var result = EditorUtility.DisplayDialogComplex(
            "VLiveKit Installer",
            $"{missingCount} VLiveKit package(s) are not installed yet.\n\n{summary}\nInstall missing packages now?",
            "Install Missing",
            "Later",
            "Status Only");

        if (result == 0)
        {
            QueueMissingPackages();
            return;
        }

        if (result == 2)
        {
            ShowStatusDialog();
        }
    }

    private static void ShowStatusDialog()
    {
        EditorUtility.DisplayDialog("VLiveKit Installer", BuildStatusSummary(), "OK");
    }

    private static void QueueMissingPackages()
    {
        PendingPackages.Clear();
        foreach (var status in LastStatuses)
        {
            if (!status.IsInstalled)
            {
                PendingPackages.Enqueue(status.Package);
            }
        }

        totalCount = PendingPackages.Count;
        installedCount = 0;

        if (totalCount == 0)
        {
            Debug.Log("All VLiveKit packages are already present.");
            EditorUtility.DisplayDialog("VLiveKit Installer", "All VLiveKit packages are already present.", "OK");
            return;
        }

        EditorApplication.update += UpdateInstallQueue;
        InstallNextPackage();
    }

    private static void UpdateInstallQueue()
    {
        if (currentRequest == null || !currentRequest.IsCompleted)
        {
            return;
        }

        if (currentRequest.Status == StatusCode.Success)
        {
            installedCount++;
            Debug.Log($"Installed {currentPackage.DisplayName}: {currentRequest.Result.packageId}");
            InstallNextPackage();
            return;
        }

        var errorMessage = currentRequest.Error != null ? currentRequest.Error.message : "Unknown Package Manager error.";
        CleanupInstallQueue();
        Debug.LogError($"Failed to install {currentPackage.DisplayName}: {errorMessage}");
        EditorUtility.DisplayDialog("VLiveKit Installer", $"Failed to install {currentPackage.DisplayName}.\n\n{errorMessage}", "OK");
    }

    private static void InstallNextPackage()
    {
        currentRequest = null;

        if (PendingPackages.Count == 0)
        {
            CleanupInstallQueue();
            Debug.Log("VLiveKit package installation completed.");
            EditorUtility.DisplayDialog("VLiveKit Installer", "Missing VLiveKit packages were added.", "OK");
            return;
        }

        currentPackage = PendingPackages.Dequeue();
        var currentNumber = installedCount + 1;
        EditorUtility.DisplayProgressBar(
            "VLiveKit Installer",
            $"Adding {currentPackage.DisplayName} ({currentNumber}/{totalCount})",
            (float)installedCount / totalCount);

        Debug.Log($"Adding {currentPackage.DisplayName} from {currentPackage.PackageUrl}");
        currentRequest = Client.Add(currentPackage.PackageUrl);
    }

    private static string BuildStatusSummary()
    {
        var builder = new StringBuilder();
        foreach (var status in LastStatuses)
        {
            builder.Append(status.IsInstalled ? "[OK] " : "[Missing] ");
            builder.Append(status.Package.DisplayName);
            builder.Append(" - ");
            builder.Append(GetStateLabel(status.State));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static int GetMissingCount()
    {
        var count = 0;
        foreach (var status in LastStatuses)
        {
            if (!status.IsInstalled)
            {
                count++;
            }
        }

        return count;
    }

    private static string ReadManifestJson()
    {
        var manifestPath = ToProjectPath("Packages/manifest.json");
        return File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : string.Empty;
    }

    private static string ToProjectPath(string unityRelativePath)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", unityRelativePath));
    }

    private static string GetStateLabel(InstallState state)
    {
        switch (state)
        {
            case InstallState.PackageManager:
                return "Package Manager";
            case InstallState.Manifest:
                return "Packages/manifest.json";
            case InstallState.PackageFolderOrSubmodule:
                return "Packages folder or submodule";
            case InstallState.AssetsFolder:
                return "Assets folder";
            default:
                return "Not found";
        }
    }

    private static void CleanupInstallQueue()
    {
        currentRequest = null;
        currentPackage = default;
        totalCount = 0;
        installedCount = 0;
        PendingPackages.Clear();
        EditorUtility.ClearProgressBar();
        EditorApplication.update -= UpdateInstallQueue;
    }

    private readonly struct PackageSpec
    {
        public PackageSpec(string packageName, string displayName, string packageUrl, string packageFolderPath, string assetFolderPath)
        {
            PackageName = packageName;
            DisplayName = displayName;
            PackageUrl = packageUrl;
            PackageFolderPath = packageFolderPath;
            AssetFolderPath = assetFolderPath;
        }

        public string PackageName { get; }
        public string DisplayName { get; }
        public string PackageUrl { get; }
        public string PackageFolderPath { get; }
        public string AssetFolderPath { get; }
    }

    private readonly struct PackageStatus
    {
        public PackageStatus(PackageSpec package, InstallState state)
        {
            Package = package;
            State = state;
        }

        public PackageSpec Package { get; }
        public InstallState State { get; }
        public bool IsInstalled => State != InstallState.Missing;
    }

    private enum InstallState
    {
        Missing,
        PackageManager,
        Manifest,
        PackageFolderOrSubmodule,
        AssetsFolder
    }

    private enum StatusCheckMode
    {
        ShowPrompt,
        ShowStatusOnly,
        InstallMissing
    }
}
#endif
