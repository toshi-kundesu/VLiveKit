# VLiveKit

VLiveKit is a small Unity Editor installer package.

This package does not depend on the individual VLiveKit packages. It adds an installer menu that checks the current project and installs missing VLiveKit packages when you choose to do so, and declares shared external package dependencies used by VLiveKit workflows.

## Menu

After the package is imported, open:

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

- Package name: `com.toshi.vlivekit`
- Unity: `2022.3`
- Repository: https://github.com/toshi-kundesu/VLiveKit

## Install

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
    "com.toshi.vlivekit": "0.1.18"
  }
}
```
