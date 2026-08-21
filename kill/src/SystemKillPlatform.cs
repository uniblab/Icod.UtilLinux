namespace Icod.UtilLinux.Kill;

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Icod.CommandFramework.Processes;

/// <summary>Provides util-linux <c>kill</c> host integrations that are not general F4 operations.</summary>
public sealed class SystemKillPlatform : IKillPlatform {
	private const int PermissionDenied = 1;
	private const int NoSuchProcess = 3;
	private const int Interrupted = 4;
	private const int InvalidArgument = 22;
	private const int FunctionNotImplemented = 38;
	private const int PollInput = 0x0001;
	private const int AtEmptyPath = 0x1000;
	private const uint StatxInode = 0x00000100;
	private const long PidFsMagic = 0x50494446;
	private const int SigInfoSize = 128;
	private const int SiQueue = -1;
	private const long PidFdSendSignalSystemCall = 424;
	private const long PidFdOpenSystemCall = 434;

	/// <summary>Gets the shared system implementation.</summary>
	public static SystemKillPlatform Instance {
		get;
	} = new();

	/// <inheritdoc />
	public bool SupportsRestrictedNameLookup => OperatingSystem.IsLinux();

	private SystemKillPlatform() {
	}

	/// <inheritdoc />
	public ProcessOperationResult<IReadOnlyList<int>> ResolveProcessName(
		string name,
		bool allUsers
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		if ( OperatingSystem.IsLinux() ) {
			return ResolveLinuxProcessName( name, allUsers );
		}
		if ( !allUsers ) {
			return ProcessOperationResult<IReadOnlyList<int>>.Failure(
				ProcessOperationStatus.Unsupported,
				"Restricted process-name lookup requires Linux /proc; use --all for the host process API."
			);
		}
		try {
			var processIds = Process.GetProcessesByName( name )
				.Select( process => {
					try {
						return process.Id;
					} finally {
						process.Dispose();
					}
				} )
				.OrderBy( processId => processId )
				.ToArray()
			;
			return ProcessOperationResult<IReadOnlyList<int>>.Success( processIds );
		} catch ( Exception exception ) when ( exception is InvalidOperationException or NotSupportedException ) {
			return ProcessOperationResult<IReadOnlyList<int>>.Failure(
				ProcessOperationStatus.Unsupported,
				exception.Message
			);
		}
	}

	/// <inheritdoc />
	public ProcessOperationResult<IReadOnlyList<KillSignalState>> ReadSignalState(
		int processId
	) {
		if ( !OperatingSystem.IsLinux() ) {
			return ProcessOperationResult<IReadOnlyList<KillSignalState>>.Failure(
				ProcessOperationStatus.Unsupported,
				"--show-process-state requires Linux /proc."
			);
		}
		if ( 0 >= processId ) {
			return ProcessOperationResult<IReadOnlyList<KillSignalState>>.Failure(
				ProcessOperationStatus.InvalidArgument,
				"A positive process identifier is required."
			);
		}
		try {
			var fields = new ( string Key, string Label )[] {
				( "SigPnd:", "Pending (thread)" ),
				( "ShdPnd:", "Pending (process)" ),
				( "SigBlk:", "Blocked" ),
				( "SigIgn:", "Ignored" ),
				( "SigCgt:", "Caught" )
			};
			var states = new List<KillSignalState>();
			var lines = File.ReadAllLines( $"/proc/{processId}/status" );
			foreach ( var field in fields ) {
				var line = lines.FirstOrDefault( candidate => candidate.StartsWith( field.Key, StringComparison.Ordinal ) );
				if ( null == line ) continue;
				if ( !ulong.TryParse(
					line[ field.Key.Length.. ].Trim(),
					NumberStyles.AllowHexSpecifier,
					CultureInfo.InvariantCulture,
					out var mask
				) ) {
					return ProcessOperationResult<IReadOnlyList<KillSignalState>>.Failure(
						ProcessOperationStatus.Failed,
						$"unexpected sigmask format in /proc/{processId}/status"
					);
				}
				if ( 0 != mask ) states.Add( new KillSignalState( field.Label, mask ) );
			}
			return ProcessOperationResult<IReadOnlyList<KillSignalState>>.Success( states );
		} catch ( FileNotFoundException ) {
			return ProcessOperationResult<IReadOnlyList<KillSignalState>>.Failure(
				ProcessOperationStatus.Vanished,
				$"process {processId} does not exist"
			);
		} catch ( DirectoryNotFoundException ) {
			return ProcessOperationResult<IReadOnlyList<KillSignalState>>.Failure(
				ProcessOperationStatus.Vanished,
				$"process {processId} does not exist"
			);
		} catch ( UnauthorizedAccessException exception ) {
			return ProcessOperationResult<IReadOnlyList<KillSignalState>>.Failure(
				ProcessOperationStatus.AccessDenied,
				exception.Message
			);
		} catch ( IOException exception ) {
			return ProcessOperationResult<IReadOnlyList<KillSignalState>>.Failure(
				ProcessOperationStatus.Failed,
				exception.Message
			);
		}
	}

	/// <inheritdoc />
	public Task<ProcessOperationResult> DeliverNativeTargetAsync(
		int nativeProcessId,
		ProcessSignal signal,
		int? queuedValue,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( signal );
		if ( cancellationToken.IsCancellationRequested ) {
			return Task.FromResult( ProcessOperationResult.Failure(
				ProcessOperationStatus.Canceled,
				"Signal delivery was canceled."
			) );
		}
		if ( OperatingSystem.IsWindows() ) {
			return Task.FromResult( ProcessOperationResult.Failure(
				ProcessOperationStatus.Unsupported,
				"Windows does not expose POSIX zero, negative process-group, or -1 signal targets."
			) );
		}
		try {
			var rc = null == queuedValue
				? Kill( nativeProcessId, signal.Number )
				: SigQueue( nativeProcessId, signal.Number, new SigVal { Pointer = new IntPtr( queuedValue.Value ) } )
			;
			return Task.FromResult( 0 == rc
				? ProcessOperationResult.Success()
				: NativeFailure( nativeProcessId, signal, Marshal.GetLastPInvokeError() )
			);
		} catch ( EntryPointNotFoundException exception ) {
			return Task.FromResult( ProcessOperationResult.Failure( ProcessOperationStatus.Unsupported, exception.Message ) );
		}
	}

	/// <inheritdoc />
	public async Task<ProcessOperationResult> DeliverPidFdAsync(
		int processId,
		ulong? expectedPidFdInode,
		ProcessSignal signal,
		int? queuedValue,
		IReadOnlyList<KillTimeout> timeouts,
		Action<ProcessSignal>? timeoutSignalObserver = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( signal );
		ArgumentNullException.ThrowIfNull( timeouts );
		if ( cancellationToken.IsCancellationRequested ) {
			return ProcessOperationResult.Failure( ProcessOperationStatus.Canceled, "PID-file-descriptor signaling was canceled." );
		}
		if ( !OperatingSystem.IsLinux() || !SupportsPidFdSyscalls() ) {
			return ProcessOperationResult.Failure(
				ProcessOperationStatus.Unsupported,
				"PID-file-descriptor signaling is supported only on Linux architectures with pidfd syscalls."
			);
		}
		var descriptor = checked( (int)SyscallPidFdOpen( PidFdOpenSystemCall, processId, 0 ) );
		if ( 0 > descriptor ) {
			return NativeFailure( processId, signal, Marshal.GetLastPInvokeError(), "pidfd_open() failed" );
		}
		try {
			if ( null != expectedPidFdInode ) {
				var pidFs = ValidatePidFs( descriptor );
				if ( !pidFs.Succeeded ) return pidFs;
				var inode = ReadPidFdInode( descriptor );
				if ( !inode.Succeeded ) return ProcessOperationResult.Failure( inode.Status, inode.Message, inode.NativeErrorCode );
				if ( inode.Value != expectedPidFdInode.Value ) {
					return ProcessOperationResult.Failure(
						ProcessOperationStatus.Reused,
						$"pidfd inode mismatch for process {processId}"
					);
				}
			}
			var sent = SendPidFdSignal(
				descriptor,
				processId,
				signal,
				0 < timeouts.Count ? queuedValue ?? signal.Number : queuedValue
			);
			if ( !sent.Succeeded ) return sent;
			foreach ( var timeout in timeouts ) {
				var wait = await WaitForPidFdAsync( descriptor, timeout.Milliseconds, cancellationToken ).ConfigureAwait( false );
				if ( ProcessOperationStatus.Vanished == wait.Status ) return ProcessOperationResult.Success();
				if ( !wait.Succeeded ) return wait;
				timeoutSignalObserver?.Invoke( timeout.Signal );
				sent = SendPidFdSignal( descriptor, processId, timeout.Signal, queuedValue ?? signal.Number );
				if ( !sent.Succeeded ) return sent;
			}
			return ProcessOperationResult.Success();
		} finally {
			Close( descriptor );
		}
	}

	private static ProcessOperationResult<IReadOnlyList<int>> ResolveLinuxProcessName(
		string name,
		bool allUsers
	) {
		try {
			var ownUid = GetUid();
			var processIds = new List<int>();
			foreach ( var directory in Directory.EnumerateDirectories( "/proc" ) ) {
				if ( !int.TryParse( System.IO.Path.GetFileName( directory ), NumberStyles.None, CultureInfo.InvariantCulture, out var processId ) ) continue;
				try {
					if ( !allUsers && ReadRealUid( processId ) != ownUid ) continue;
					var processName = File.ReadAllText( $"/proc/{processId}/comm" ).TrimEnd( '\r', '\n' );
					if ( string.Equals( processName, name, StringComparison.Ordinal ) ) processIds.Add( processId );
				} catch ( IOException ) {
					// Processes may legitimately vanish while /proc is enumerated.
				} catch ( UnauthorizedAccessException ) {
					// An inaccessible process is not a match available to this invocation.
				}
			}
			processIds.Sort();
			return ProcessOperationResult<IReadOnlyList<int>>.Success( processIds );
		} catch ( IOException exception ) {
			return ProcessOperationResult<IReadOnlyList<int>>.Failure( ProcessOperationStatus.Failed, exception.Message );
		} catch ( UnauthorizedAccessException exception ) {
			return ProcessOperationResult<IReadOnlyList<int>>.Failure( ProcessOperationStatus.AccessDenied, exception.Message );
		}
	}

	private static uint ReadRealUid(
		int processId
	) {
		var line = File.ReadLines( $"/proc/{processId}/status" )
			.FirstOrDefault( candidate => candidate.StartsWith( "Uid:", StringComparison.Ordinal ) )
		;
		if ( null == line ) throw new IOException( $"/proc/{processId}/status does not contain a Uid field." );
		var tokens = line[ 4.. ].Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries );
		if ( 0 == tokens.Length || !uint.TryParse( tokens[ 0 ], NumberStyles.None, CultureInfo.InvariantCulture, out var userId ) ) {
			throw new IOException( $"/proc/{processId}/status contains an invalid Uid field." );
		}
		return userId;
	}

	private static bool SupportsPidFdSyscalls() => RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

	private static ProcessOperationResult ValidatePidFs(
		int descriptor
	) {
		var buffer = Marshal.AllocHGlobal( 128 );
		try {
			for ( var offset = 0; offset < 128; offset += sizeof( long ) ) Marshal.WriteInt64( buffer, offset, 0 );
			if ( 0 != FStatFs( descriptor, buffer ) ) {
				var error = Marshal.GetLastPInvokeError();
				return ProcessOperationResult.Failure( MapErrno( error ), "fstatfs() failed for pidfd", error );
			}
			if ( PidFsMagic != Marshal.ReadInt64( buffer, 0 ) ) {
				return ProcessOperationResult.Failure(
					ProcessOperationStatus.Unsupported,
					"PID-plus-pidfd-inode validation requires a Linux pidfs-backed pidfd."
				);
			}
			return ProcessOperationResult.Success();
		} catch ( EntryPointNotFoundException exception ) {
			return ProcessOperationResult.Failure( ProcessOperationStatus.Unsupported, exception.Message );
		} finally {
			Marshal.FreeHGlobal( buffer );
		}
	}

	private static ProcessOperationResult<ulong> ReadPidFdInode(
		int descriptor
	) {
		var buffer = Marshal.AllocHGlobal( 256 );
		try {
			for ( var offset = 0; offset < 256; offset += sizeof( long ) ) Marshal.WriteInt64( buffer, offset, 0 );
			try {
				if ( 0 != Statx( descriptor, string.Empty, AtEmptyPath, StatxInode, buffer ) ) {
					var error = Marshal.GetLastPInvokeError();
					return ProcessOperationResult<ulong>.Failure( MapErrno( error ), "statx() failed for pidfd", error );
				}
			} catch ( EntryPointNotFoundException exception ) {
				return ProcessOperationResult<ulong>.Failure( ProcessOperationStatus.Unsupported, exception.Message );
			}
			return ProcessOperationResult<ulong>.Success( unchecked( (ulong)Marshal.ReadInt64( buffer, 32 ) ) );
		} finally {
			Marshal.FreeHGlobal( buffer );
		}
	}

	private static ProcessOperationResult SendPidFdSignal(
		int descriptor,
		int processId,
		ProcessSignal signal,
		int? queuedValue
	) {
		var info = IntPtr.Zero;
		try {
			if ( null != queuedValue ) {
				info = Marshal.AllocHGlobal( SigInfoSize );
				for ( var offset = 0; offset < SigInfoSize; offset += sizeof( long ) ) Marshal.WriteInt64( info, offset, 0 );
				Marshal.WriteInt32( info, 0, signal.Number );
				Marshal.WriteInt32( info, 4, 0 );
				Marshal.WriteInt32( info, 8, SiQueue );
				Marshal.WriteInt32( info, 16, Environment.ProcessId );
				Marshal.WriteInt32( info, 20, unchecked( (int)GetUid() ) );
				Marshal.WriteInt32( info, 24, queuedValue.Value );
			}
			if ( 0 == SyscallPidFdSendSignal( PidFdSendSignalSystemCall, descriptor, signal.Number, info, 0 ) ) {
				return ProcessOperationResult.Success();
			}
			return NativeFailure( processId, signal, Marshal.GetLastPInvokeError(), "pidfd_send_signal() failed" );
		} finally {
			if ( IntPtr.Zero != info ) Marshal.FreeHGlobal( info );
		}
	}

	private static async Task<ProcessOperationResult> WaitForPidFdAsync(
		int descriptor,
		int milliseconds,
		CancellationToken cancellationToken
	) {
		if ( 0 > milliseconds ) {
			return ProcessOperationResult.Failure( ProcessOperationStatus.InvalidArgument, "timeout must be non-negative" );
		}
		var stopwatch = Stopwatch.StartNew();
		while ( true ) {
			if ( cancellationToken.IsCancellationRequested ) {
				return ProcessOperationResult.Failure( ProcessOperationStatus.Canceled, "PID-file-descriptor wait was canceled." );
			}
			var remaining = milliseconds - (int)Math.Min( int.MaxValue, stopwatch.ElapsedMilliseconds );
			if ( 0 >= remaining ) return ProcessOperationResult.Success();
			var slice = Math.Min( remaining, 100 );
			var descriptors = new[] {
				new PollDescriptor { FileDescriptor = descriptor, Events = PollInput, ReturnedEvents = 0 }
			};
			var rc = Poll( descriptors, (nuint)descriptors.Length, slice );
			if ( 0 < rc ) {
				return ProcessOperationResult.Failure( ProcessOperationStatus.Vanished, "The process exited before the follow-up signal." );
			}
			if ( 0 > rc ) {
				var error = Marshal.GetLastPInvokeError();
				if ( Interrupted == error ) continue;
				return ProcessOperationResult.Failure( MapErrno( error ), "poll() failed for pidfd", error );
			}
			await Task.Yield();
		}
	}

	private static ProcessOperationResult NativeFailure(
		int processId,
		ProcessSignal signal,
		int error,
		string? prefix = null
	) => ProcessOperationResult.Failure(
		MapErrno( error ),
		$"{prefix ?? "sending signal"} {signal.Number} to {processId}: errno {error}",
		error
	);

	private static ProcessOperationStatus MapErrno(
		int error
	) => error switch {
		NoSuchProcess => ProcessOperationStatus.Vanished,
		PermissionDenied => ProcessOperationStatus.AccessDenied,
		InvalidArgument => ProcessOperationStatus.InvalidArgument,
		FunctionNotImplemented => ProcessOperationStatus.Unsupported,
		_ => ProcessOperationStatus.Failed
	};

	[StructLayout( LayoutKind.Sequential )]
	private struct SigVal {
		/// <summary>Stores the pointer-sized native signal value union.</summary>
		internal IntPtr Pointer;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct PollDescriptor {
		/// <summary>Stores the native file descriptor.</summary>
		internal int FileDescriptor;
		/// <summary>Stores requested native poll events.</summary>
		internal short Events;
		/// <summary>Stores returned native poll events.</summary>
		internal short ReturnedEvents;
	}

	[DllImport( "libc", EntryPoint = "getuid", SetLastError = false )]
	private static extern uint GetUid();

	[DllImport( "libc", EntryPoint = "kill", SetLastError = true )]
	private static extern int Kill( int processId, int signal );

	[DllImport( "libc", EntryPoint = "sigqueue", SetLastError = true )]
	private static extern int SigQueue( int processId, int signal, SigVal value );

	[DllImport( "libc", EntryPoint = "close", SetLastError = true )]
	private static extern int Close( int descriptor );

	[DllImport( "libc", EntryPoint = "poll", SetLastError = true )]
	private static extern int Poll( [In, Out] PollDescriptor[] descriptors, nuint count, int timeout );

	[DllImport( "libc", EntryPoint = "fstatfs", SetLastError = true )]
	private static extern int FStatFs( int descriptor, IntPtr buffer );

	[DllImport( "libc", EntryPoint = "statx", SetLastError = true )]
	private static extern int Statx( int directoryDescriptor, string path, int flags, uint mask, IntPtr buffer );

	[DllImport( "libc", EntryPoint = "syscall", SetLastError = true )]
	private static extern long SyscallPidFdOpen( long number, int processId, uint flags );

	[DllImport( "libc", EntryPoint = "syscall", SetLastError = true )]
	private static extern long SyscallPidFdSendSignal( long number, int descriptor, int signal, IntPtr information, uint flags );
}
