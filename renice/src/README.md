# renice

`Icod.UtilLinux.Renice` implements the util-linux 2.42.2 `renice` profile.

The command consumes `Icod.CommandFramework.Processes.IProcessPrioritySelectorProvider` for process, process-group, and user priority operations and `Icod.CommandFramework.Platform.IIdentityProvider` for username resolution. It contains no command-local priority native calls.

On POSIX hosts the framework provider maps these selectors to `getpriority(2)` and `setpriority(2)`. Windows supports individual-process priority classes as a documented approximation; process-group and user targets return controlled `Unsupported` results.
