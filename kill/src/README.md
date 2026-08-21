# util-linux kill implementation

This directory implements the util-linux 2.42.2 `kill(1)` command profile. `Command.cs` owns command parsing, status aggregation, and presentation. `KillModels.cs` defines the injectable command-local contracts used for util-linux-specific process discovery and pidfd operations. `SystemKillPlatform.cs` implements Linux `/proc`, `sigqueue(3)`, and pidfd behavior while ordinary positive-PID and negative process-group delivery uses the general process contracts in `Icod.CommandFramework.Processes`.

The pidfd path is used for `PID:PIDFD_INODE` operands and every `--timeout` sequence so delayed follow-up signals cannot be redirected to a recycled PID. Unsupported native behavior on Windows, macOS, BSD, older Linux kernels, or unsupported Linux architectures is reported as a controlled command failure rather than silently approximated.
