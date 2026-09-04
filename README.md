# Icod.UtilLinux

[![PR Staging build](https://github.com/uniblab/Icod.UtilLinux/actions/workflows/pull-request.yaml/badge.svg?event=pull_request)](https://github.com/uniblab/Icod.UtilLinux/actions/workflows/pull-request.yaml)
[![Main Release validation](https://github.com/uniblab/Icod.UtilLinux/actions/workflows/main.yaml/badge.svg?branch=main)](https://github.com/uniblab/Icod.UtilLinux/actions/workflows/main.yaml)

`Icod.UtilLinux` contains .NET implementations of selected util-linux commands extracted from the former multi-suite `Icod.CoreUtils` development repository.

Current commands:

- `kill`
- `renice`

The projects target `net10.0` with C# 13 and depend on the published `Icod.CommandFramework` package for neutral process-control and platform identity contracts. This repository does not depend on `Icod.CoreUtils.Shared`.

## Build and test

On Windows:

```text
build.cmd
```

On Unix-like hosts:

```text
./build.sh
```

The wrappers use `Debug` by default and run clean → restore → build → test → pack → validate. Individual `clean`, `restore`, `build`, `test`, `pack`, and `validate` stages may also be requested.

The CI/CD lifecycle follows the canonical `uniblab/.github` pattern: pull requests use `Staging` on Windows/Linux/macOS, pushes to `main` use six-runner `Release` distribution validation, and `v<semver>` tags drive package/archive publication.

## Versioning

The repository version is centralized in [`Directory.Build.props`](Directory.Build.props). The current version is `1.0.1`; production projects inherit `Version`, `PackageVersion`, `AssemblyVersion`, and `FileVersion` from that single source.

See [`packaging/README.md`](packaging/README.md) for build, validation, packaging, and release details.

The executable assembly names remain lowercase (`kill` and `renice`) to preserve command identity.
