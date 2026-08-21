# RENICE(1)

## NAME

**renice** — alter the scheduling priority of running processes

## SYNOPSIS

```text
renice [-n|--priority|--relative] <priority> [-p|--pid] <pid>...
renice [-n|--priority|--relative] <priority> -g|--pgrp <pgid>...
renice [-n|--priority|--relative] <priority> -u|--user <user>...
```

## DESCRIPTION

`Icod.UtilLinux.Renice` is a .NET implementation of the util-linux `renice(1)` command profile, currently modeled on util-linux 2.42.2.

The command changes the nice value associated with processes, process groups, or users. It preserves ordered selector changes on the command line and reports each successful priority transition.

Priority operations use `Icod.CommandFramework.Processes.IProcessPrioritySelectorProvider`. Username and user-identity resolution use `Icod.CommandFramework.Platform.IIdentityProvider`. The project therefore contains no command-local implementation of the general priority or identity mechanisms.

## OPTIONS

```text
-n <num>
    Specify an absolute nice value by default. When POSIXLY_CORRECT is set,
    interpret the value as relative.

--priority <num>
    Specify an absolute nice value regardless of POSIXLY_CORRECT.

--relative <num>
    Add the supplied value to the current nice value.

-p, --pid
    Interpret following operands as process IDs. This is the default selector.

-g, --pgrp
    Interpret following operands as process-group IDs.

-u, --user
    Interpret following operands as user names or numeric user IDs.

-h, --help
    Display command help.

-v, -V, --version
    Display version information.
```

## PRIORITY VALUES

Requested absolute and relative results are constrained to the conventional POSIX nice range from `-20` through `19`.

For user operands, name lookup is attempted before numeric interpretation. Numeric process, process-group, and user selectors preserve zero where the underlying host API defines a current-target meaning.

## PLATFORM NOTES

On POSIX hosts, the framework provider maps priority operations to `getpriority(2)` and `setpriority(2)` semantics.

On Windows, individual-process priority changes are represented through the framework's documented host approximation. Process-group and user priority selectors may return `Unsupported` when the host cannot represent the requested POSIX operation.

## EXIT STATUS

```text
0   Every requested priority change succeeded.
1   The invocation was invalid or one or more requested priority operations failed.
```

A failed target does not prevent later targets from being attempted.

## AUTHORS

Inspired by original work from the BSD 4.0 and The Regents of the University of California, and the util-linux project and its contributors.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `renice.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`renice(1)`, `nice(1)`, `getpriority(2)`, `setpriority(2)`, `kill(1)`
