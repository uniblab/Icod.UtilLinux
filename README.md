# Icod.UtilLinux

`Icod.UtilLinux` contains .NET implementations of selected util-linux commands extracted from the former multi-suite `Icod.CoreUtils` development repository.

Current commands:

- `kill`
- `renice`

The projects target `net10.0` with C# 13 and depend on the published `Icod.CommandFramework` package for neutral process-control and platform identity contracts. This repository does not depend on `Icod.CoreUtils.Shared`.

## Build and test

```text
dotnet restore Icod.UtilLinux.sln
dotnet build Icod.UtilLinux.sln -c Release --no-restore
dotnet test Icod.UtilLinux.sln -c Release --no-build --logger trx
```

CI runs on Windows, Ubuntu, and macOS.

The executable assembly names remain lowercase (`kill` and `renice`) to preserve command identity.
