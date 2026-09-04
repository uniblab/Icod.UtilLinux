# Icod.UtilLinux build and packaging

This repository follows the canonical `uniblab/.github` .NET lifecycle.

- Local `build.cmd` / `build.sh`: `Debug`, running clean → restore → build → test → pack → validate.
- Pull requests: `Staging` on Windows, Linux, and macOS; Linux additionally validates generated packages.
- `main`: `Release` distribution validation on Windows/Linux/macOS, x64 and ARM64.
- Manual distribution validation: selectable Debug/Staging/Release across the same six runners.
- `v<semver>` tags: Release package selection/publication plus RID archives and checksums.

Repository versioning is centralized in `/Directory.Build.props`; the current version is `1.0.1`. Production projects must not duplicate the repository version locally.

The release archive builder discovers executable projects from the solution and currently stages `kill` and `renice` together with the root README and LICENSE.
