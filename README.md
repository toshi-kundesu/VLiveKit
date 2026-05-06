# VLiveKit

VLiveKit is a small Unity Editor installer package.

This package does not depend on the individual VLiveKit packages. It only adds an installer menu that checks the current project and installs missing VLiveKit packages when you choose to do so.

## Menu

After the package is imported, open:

`toshi > VLiveKit > Check Install Status`

or:

`toshi > VLiveKit > Install Missing Packages`

## What It Checks

The installer checks:

- Unity Package Manager
- `Packages/manifest.json`
- local `Packages` folders or submodules
- matching `Assets/toshi.VLiveKit` folders

It only adds packages that are not already present.

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
    "com.toshi.vlivekit": "0.1.3"
  }
}
```
