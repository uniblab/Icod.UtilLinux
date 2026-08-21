# KILL(1)

## NAME

**kill** — send a signal to a process or process group

## SYNOPSIS

```text
kill [options] <pid>|<pid>:<pidfd_inode>|<name>...
```

## DESCRIPTION

`Icod.UtilLinux.Kill` is a .NET implementation of the util-linux `kill(1)` command profile, currently modeled on util-linux 2.42.2.

The command sends signals to processes and process groups, translates and lists signal names, resolves process names, supports queued signal values, and implements Linux pidfd-backed timeout delivery so delayed follow-up signals are not accidentally sent to a recycled PID.

Ordinary process signaling and process inspection use the neutral process-control contracts provided by `Icod.CommandFramework`. Linux-specific process discovery, `/proc` inspection, `sigqueue(3)`, and pidfd operations remain command-local host integrations.

## OPTIONS

```text
-a, --all
    Do not restrict process-name lookup to processes owned by the same user.

-s, --signal <signal>
    Send the specified signal instead of SIGTERM.

-q, --queue <value>
    Send an integer value with the signal when the host supports queued delivery.

--timeout <milliseconds> <signal>
    Wait for the specified interval and send a follow-up signal through a
    Linux pidfd-protected path. May be specified more than once.

-p, --pid
    Resolve and print process IDs without sending a signal.

-l, --list[=<signal>|=0x<sigmask>]
    List signals, translate a signal or shell-style 128+signal status value,
    or decode a hexadecimal signal mask.

-L, --table
    Display signal names and numbers.

-r, --require-handler
    Signal only processes that have a userspace handler installed.

-d, --show-process-state <pid>
    Display signal-related fields from /proc/PID/status on Linux.

--verbose
    Report prospective signal deliveries.

-h, --help
    Display command help.

-V, --version
    Display version information.
```

## OPERANDS

A target may be a numeric process identifier, a process name, a native zero or negative process-group target where supported, or Linux `PID:PIDFD_INODE` syntax for pidfd identity validation.

Signal names may be supplied with or without the `SIG` prefix. Linux realtime signal forms are supported according to the command parser's util-linux compatibility rules.

## PLATFORM NOTES

On Linux, the implementation uses `/proc`, native signal APIs, and pidfd syscalls for features that require them. On Windows and other hosts, unsupported POSIX-specific behaviors return controlled failures rather than silently approximating semantics.

## EXIT STATUS

```text
0   All requested operations succeeded.
1   The command failed, all attempted targets failed, or the invocation was invalid.
64  Some requested targets succeeded and some failed.
```

## AUTHORS

Inspired by original work from the util-linux project and its contributors.

Migrated to .Net by Timothy J. Bruce.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `kill.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`kill(1)`, `signal(7)`, `sigqueue(3)`, `pidfd_open(2)`, `pidfd_send_signal(2)`, `renice(1)`
