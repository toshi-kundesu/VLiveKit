# VLiveKit

VLiveKit is a Unity feature set for installing the related VLiveKit packages together.

Install this package when you want the virtual live production toolchain as one group instead of adding each module one by one.

The related VLiveKit packages are resolved from the configured scoped registry.

## Package

- Package name: `com.toshi.vlivekit`
- Version: `0.0.1`
- Unity: `2022.3`
- Repository: https://github.com/toshi-kundesu/VLiveKit

## Included Packages

- VLiveKit ArtNetLink
- VLive Camera Unit
- VLiveKit LED Vision
- VLive Lens Filters
- VLive Live Toon
- VLive Performer Act
- VLiveKit StageBuilder
- VLiveKit StageEffect
- VLiveKit Test Assets Container
- VLive Third Party Utilities
- VLiveKit VideoRack

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
    "com.toshi.vlivekit": "0.0.1"
  }
}
```

Unity Package Manager also shows this package under `Packages: My Registries` when the `toshi` scoped registry is configured.

## Notes

This package is a feature set / aggregate package. It does not contain runtime assets by itself; it depends on the individual VLiveKit packages.
