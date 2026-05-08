# VLiveKit Package Manager

VLiveKit Package Manager is a small Unity Editor package for installing and updating VLiveKit packages.

This package does not depend on the individual VLiveKit packages. It adds an installer menu that checks the current project and installs missing VLiveKit packages when you choose to do so.

## Menu

After the package is imported, open:

`toshi > VLiveKit Package Manager`

The legacy menu is also available at:

`toshi > VLiveKit > Package Manager`

The window can:

- refresh the package catalog from the latest published `com.toshi.vlivekit` package
- check installed package versions
- check the latest versions from the npm registry
- open package GitHub repositories and documentation
- install missing packages
- update installed registry packages
- show update progress while batch operations are running
- detect local `Packages` folders, submodules, and `Assets/toshi.VLiveKit` folders without replacing them automatically

Error logs can be exported from:

`toshi > VLiveKit > Error Log Exporter`

The exporter can copy or write Unity Console errors to `Logs/VLiveKitConsoleLogs` so they can be shared without selecting console entries one by one.

## Package

- Display name: `VLiveKit Package Manager`
- Package ID: `com.toshi.vlivekit`
- Unity: `2022.3`
- Repository: https://github.com/toshi-kundesu/VLiveKit

## Install

### Bootstrap unitypackage

Import `VLiveKitPackageManagerBootstrap.unitypackage` into the Unity project.

Unity adds a bootstrap script under `Assets/VLiveKitPackageManagerBootstrap/Editor/` and shows a `VLiveKitPackageManager` prompt. Choose `Set Up` to add the `com.toshi` scoped registry and install `com.toshi.vlivekit` with Unity Package Manager.

Unity may show a `Missing Signature` notice for scoped registry packages. This is expected for packages installed from the npm scoped registry.

If the project is not managed with Git, make a project backup before installing packages.

After installation, open:

`toshi > VLiveKit Package Manager`

### Manual manifest setup

Configure the scoped registry in `Packages/manifest.json`.

```json
{
  "scopedRegistries": [
    {
      "name": "toshi",
      "url": "https://registry.npmjs.org",
      "scopes": [
        "com.toshi"
      ]
    },
    {
      "name": "npmjs",
      "url": "https://registry.npmjs.com",
      "scopes": [
        "jp.keijiro",
        "com.hecomi"
      ]
    }
  ],
  "dependencies": {
    "com.toshi.vlivekit": "0.1.24"
  }
}
```
