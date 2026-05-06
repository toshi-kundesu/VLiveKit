using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Toshi.VLiveKit.PackageInstaller.Editor
{
    public static class VLiveKitPackageInstaller
    {
        private static readonly string[] PackageUrls =
        {
            "https://github.com/toshi-kundesu/VLiveKit_TestAssetsContainer.git?path=/Assets/toshi.VLiveKit/TestAssetsContainer",
            "https://github.com/toshi-kundesu/VLiveKit_LiveLensFilters.git?path=/Assets/toshi.VLiveKit/LiveLensFilters",
            "https://github.com/toshi-kundesu/VLiveKit_camera.git?path=/Assets/toshi.VLiveKit/VLiveCameraUnit",
            "https://github.com/toshi-kundesu/VLiveKit_LEDVision.git?path=/Assets/toshi.VLiveKit/LEDVision",
            "https://github.com/toshi-kundesu/VLiveKit_ArtNetLink.git?path=/Assets/toshi.VLiveKit/ArtNetLink",
            "https://github.com/toshi-kundesu/VLiveKit_LiveToon.git?path=/Assets/toshi.VLiveKit/livetoon",
            "https://github.com/toshi-kundesu/VLiveKit_PerformerAct.git?path=/Assets/toshi.VLiveKit/PerformerAct",
            "https://github.com/toshi-kundesu/VLiveKit_StageBuilder.git?path=/Assets/toshi.VLiveKit/StageBuilder",
            "https://github.com/toshi-kundesu/VLiveKit_ThirdPartyUtilities.git?path=/Assets/toshi.VLiveKit/ThirdPartyUtilities"
        };

        private static Queue<string> pendingPackages;
        private static AddRequest currentRequest;
        private static string currentPackage;

        [MenuItem("Tools/VLiveKit/Install All Packages")]
        public static void InstallAllPackages()
        {
            if (currentRequest != null)
            {
                Debug.LogWarning("VLiveKit package installation is already running.");
                return;
            }

            pendingPackages = new Queue<string>(PackageUrls);
            EditorApplication.update += Update;
            AddNextPackage();
        }

        private static void Update()
        {
            if (currentRequest == null || !currentRequest.IsCompleted)
            {
                return;
            }

            if (currentRequest.Status == StatusCode.Success)
            {
                Debug.Log($"Installed VLiveKit package: {currentPackage}");
            }
            else
            {
                Debug.LogError($"Failed to install VLiveKit package: {currentPackage}\n{currentRequest.Error.message}");
            }

            currentRequest = null;
            currentPackage = null;
            AddNextPackage();
        }

        private static void AddNextPackage()
        {
            if (pendingPackages == null || pendingPackages.Count == 0)
            {
                EditorApplication.update -= Update;
                pendingPackages = null;
                Debug.Log("VLiveKit package installation finished.");
                return;
            }

            currentPackage = pendingPackages.Dequeue();
            Debug.Log($"Installing VLiveKit package: {currentPackage}");
            currentRequest = Client.Add(currentPackage);
        }
    }
}
