# VLiveKit

VLiveKit is a small Unity Editor installer package.

This package does not depend on the individual VLiveKit packages. It only adds an installer menu that checks the current project and installs missing VLiveKit packages when you choose to do so.

## Menu

After the package is imported, open:

`toshi > VLiveKit > Package Manager`

The window can:

- check installed package versions
- check the latest versions from the npm registry
- install missing packages
- update installed registry packages
- detect local `Packages` folders, submodules, and `Assets/toshi.VLiveKit` folders without replacing them automatically

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
    }
  ],
  "dependencies": {
    "com.toshi.vlivekit": "0.1.5"
  }
}
```
