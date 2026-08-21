namespace Icod.UtilLinux.Kill;

using Icod.CommandFramework.Processes;

/// <summary>Describes one delayed follow-up signal requested with <c>--timeout</c>.</summary>
public sealed record KillTimeout {
	/// <summary>Initializes one delayed follow-up signal.</summary>
	public KillTimeout(
		int milliseconds,
		ProcessSignal signal
	) {
		this.Milliseconds = milliseconds;
		this.Signal = signal ?? throw new ArgumentNullException( nameof( signal ) );
	}

	/// <summary>Gets the delay before the follow-up signal, in milliseconds.</summary>
	public int Milliseconds {
		get;
	}

	/// <summary>Gets the signal sent after the delay expires.</summary>
	public ProcessSignal Signal {
		get;
	}
}

/// <summary>Describes one resolved command operand.</summary>
public sealed record KillResolvedTarget {
	/// <summary>Initializes one resolved target.</summary>
	public KillResolvedTarget(
		string operand,
		int nativeProcessId,
		ulong? expectedPidFdInode = null
	) {
		ArgumentException.ThrowIfNullOrEmpty( operand );
		this.Operand = operand;
		this.NativeProcessId = nativeProcessId;
		this.ExpectedPidFdInode = expectedPidFdInode;
	}

	/// <summary>Gets the original command-line operand.</summary>
	public string Operand {
		get;
	}

	/// <summary>Gets the native PID convention value, including zero or negative group targets.</summary>
	public int NativeProcessId {
		get;
	}

	/// <summary>Gets the expected pidfd inode when the operand used PID:PIDFD_INODE syntax.</summary>
	public ulong? ExpectedPidFdInode {
		get;
	}
}

/// <summary>Describes one decoded Linux process signal-state field.</summary>
public sealed record KillSignalState {
	/// <summary>Initializes one decoded signal-state field.</summary>
	public KillSignalState(
		string label,
		ulong mask
	) {
		ArgumentException.ThrowIfNullOrEmpty( label );
		this.Label = label;
		this.Mask = mask;
	}

	/// <summary>Gets the presentation label.</summary>
	public string Label {
		get;
	}

	/// <summary>Gets the native signal bit mask.</summary>
	public ulong Mask {
		get;
	}
}

/// <summary>
/// Supplies the util-linux-specific process discovery and Linux pidfd operations that sit above
/// the general process-control contracts in <c>Icod.CommandFramework</c>.
/// </summary>
public interface IKillPlatform {
	/// <summary>Gets whether process-name discovery is available with same-user filtering.</summary>
	bool SupportsRestrictedNameLookup {
		get;
	}

	/// <summary>Resolves every process with an exact command name.</summary>
	ProcessOperationResult<IReadOnlyList<int>> ResolveProcessName(
		string name,
		bool allUsers
	);

	/// <summary>Reads Linux signal masks from <c>/proc/PID/status</c>.</summary>
	ProcessOperationResult<IReadOnlyList<KillSignalState>> ReadSignalState(
		int processId
	);

	/// <summary>Sends a native target that requires signed-PID or queued-value semantics beyond the general F4 target model.</summary>
	Task<ProcessOperationResult> DeliverNativeTargetAsync(
		int nativeProcessId,
		ProcessSignal signal,
		int? queuedValue,
		CancellationToken cancellationToken = default
	);

	/// <summary>
	/// Sends a signal to a Linux pidfd-protected target and, when requested, performs sequential
	/// pidfd-polled timeout follow-ups without exposing PID-reuse races.
	/// </summary>
	Task<ProcessOperationResult> DeliverPidFdAsync(
		int processId,
		ulong? expectedPidFdInode,
		ProcessSignal signal,
		int? queuedValue,
		IReadOnlyList<KillTimeout> timeouts,
		Action<ProcessSignal>? timeoutSignalObserver = null,
		CancellationToken cancellationToken = default
	);
}
