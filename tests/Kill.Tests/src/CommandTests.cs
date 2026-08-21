namespace Icod.UtilLinux.Kill.Tests;

using Icod.CommandFramework.Processes;
using Xunit;

/// <summary>Tests the util-linux 2.42.2 <c>kill</c> command profile.</summary>
public sealed class CommandTests {
	/// <summary>Verifies the default TERM signal is delivered through the F4 provider.</summary>
	[Fact]
	public async Task SendsDefaultTermToPositivePid() {
		var signals = new FakeSignalProvider();
		var exitCode = await Command.RunAsync(
			[ "123" ],
			new StringWriter(),
			new StringWriter(),
			signals,
			new FakeInspector(),
			new FakePlatform()
		);
		Assert.Equal( 0, exitCode );
		var delivery = Assert.Single( signals.Deliveries );
		Assert.Equal( 123, delivery.Target.Identifier );
		Assert.Equal( 15, delivery.Signal.Number );
	}

	/// <summary>Verifies explicit signal selection makes a negative operand a process-group target.</summary>
	[Fact]
	public async Task ExplicitSignalAllowsNegativeProcessGroupOperand() {
		var platform = new FakePlatform();
		var signals = new FakeSignalProvider();
		var exitCode = await Command.RunAsync(
			[ "-s", "INT", "-42" ],
			new StringWriter(),
			new StringWriter(),
			signals,
			new FakeInspector(),
			platform
		);
		Assert.Equal( 0, exitCode );
		var delivery = Assert.Single( signals.Deliveries );
		Assert.Equal( ProcessTargetKind.ProcessGroup, delivery.Target.Kind );
		Assert.Equal( 42, delivery.Target.Identifier );
		Assert.Equal( 2, delivery.Signal.Number );
		Assert.Empty( platform.NativeDeliveries );
	}

	/// <summary>Verifies PID zero preserves the native current-process-group convention.</summary>
	[Fact]
	public async Task ZeroTargetUsesNativeConvention() {
		var platform = new FakePlatform();
		var exitCode = await Command.RunAsync(
			[ "--", "0" ], new StringWriter(), new StringWriter(), new FakeSignalProvider(), new FakeInspector(), platform
		);
		Assert.Equal( 0, exitCode );
		Assert.Equal( 0, Assert.Single( platform.NativeDeliveries ).ProcessId );
	}

	/// <summary>Verifies PID -1 preserves the native all-permitted-processes convention.</summary>
	[Fact]
	public async Task MinusOneTargetUsesNativeConvention() {
		var platform = new FakePlatform();
		var exitCode = await Command.RunAsync(
			[ "--", "-1" ], new StringWriter(), new StringWriter(), new FakeSignalProvider(), new FakeInspector(), platform
		);
		Assert.Equal( 0, exitCode );
		Assert.Equal( -1, Assert.Single( platform.NativeDeliveries ).ProcessId );
	}

	/// <summary>Verifies util-linux numeric signal syntax does not accept a leading plus sign.</summary>
	[Fact]
	public async Task RejectsSignedPositiveSignalNumber() {
		var error = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "-s", "+15", "77" ], new StringWriter(), error, new FakeSignalProvider(), new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 1, exitCode );
		Assert.Contains( "unknown signal", error.ToString(), StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>Verifies signal zero is preserved as an error-checking signal.</summary>
	[Fact]
	public async Task SupportsSignalZero() {
		var signals = new FakeSignalProvider();
		var exitCode = await Command.RunAsync(
			[ "-s", "0", "77" ], new StringWriter(), new StringWriter(), signals, new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 0, exitCode );
		Assert.Equal( 0, Assert.Single( signals.Deliveries ).Signal.Number );
	}

	/// <summary>Verifies bare RTMIN and RTMAX are table labels, not accepted util-linux signal operands.</summary>
	[Theory]
	[InlineData( "RTMIN" )]
	[InlineData( "RTMAX" )]
	public async Task RejectsBareRealtimeBoundaryLabels( string signal ) {
		var error = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "-s", signal, "77" ], new StringWriter(), error, new FakeSignalProvider(), new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 1, exitCode );
		Assert.Contains( "unknown signal", error.ToString(), StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>Verifies shell-style 128+signal status translation for -l.</summary>
	[Fact]
	public async Task ListTranslatesShellStatus() {
		var output = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "-l", "143" ], output, new StringWriter(), new FakeSignalProvider(), new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 0, exitCode );
		Assert.Equal( string.Concat( "TERM", Environment.NewLine ), output.ToString() );
	}

	/// <summary>Verifies hexadecimal signal masks are decoded one name per line.</summary>
	[Fact]
	public async Task ListDecodesSignalMask() {
		var output = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "-l", "0x4002" ], output, new StringWriter(), new FakeSignalProvider(), new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 0, exitCode );
		Assert.Equal( string.Concat( "INT", Environment.NewLine, "TERM", Environment.NewLine ), output.ToString() );
	}

	/// <summary>Verifies command names and PIDs may be mixed and name lookup defaults to the same user.</summary>
	[Fact]
	public async Task MixesNameAndPidTargets() {
		var platform = new FakePlatform();
		platform.Names[ "worker" ] = [ 10, 11 ];
		var signals = new FakeSignalProvider();
		var exitCode = await Command.RunAsync(
			[ "worker", "12" ], new StringWriter(), new StringWriter(), signals, new FakeInspector(), platform
		);
		Assert.Equal( 0, exitCode );
		Assert.False( platform.LastAllUsers );
		Assert.Equal( [ 10, 11, 12 ], signals.Deliveries.Select( item => item.Target.Identifier ).ToArray() );
	}

	/// <summary>Verifies --all is passed to process-name resolution.</summary>
	[Fact]
	public async Task AllDisablesSameUserRestriction() {
		var platform = new FakePlatform();
		platform.Names[ "worker" ] = [ 10 ];
		var exitCode = await Command.RunAsync(
			[ "--all", "worker" ], new StringWriter(), new StringWriter(), new FakeSignalProvider(), new FakeInspector(), platform
		);
		Assert.Equal( 0, exitCode );
		Assert.True( platform.LastAllUsers );
	}

	/// <summary>Verifies --pid prints resolved IDs and performs no signal delivery.</summary>
	[Fact]
	public async Task PidOnlyPrintsWithoutSignaling() {
		var platform = new FakePlatform();
		platform.Names[ "worker" ] = [ 21, 22 ];
		var signals = new FakeSignalProvider();
		var output = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "--pid", "worker" ], output, new StringWriter(), signals, new FakeInspector(), platform
		);
		Assert.Equal( 0, exitCode );
		Assert.Equal( string.Concat( "21", Environment.NewLine, "22", Environment.NewLine ), output.ToString() );
		Assert.Empty( signals.Deliveries );
	}

	/// <summary>Verifies util-linux verbose output still precedes --pid output without delivering a signal.</summary>
	[Fact]
	public async Task VerbosePidOnlyReportsProspectiveSignal() {
		var signals = new FakeSignalProvider();
		var output = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "--verbose", "--pid", "44" ], output, new StringWriter(), signals, new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 0, exitCode );
		Assert.Equal(
			string.Concat( "sending signal 15 to pid 44", Environment.NewLine, "44", Environment.NewLine ),
			output.ToString()
		);
		Assert.Empty( signals.Deliveries );
	}

	/// <summary>Verifies --require-handler is evaluated before --pid output.</summary>
	[Fact]
	public async Task RequireHandlerAlsoFiltersPidOnlyOutput() {
		var signals = new FakeSignalProvider { Disposition = ProcessSignalDisposition.Default };
		var output = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "--pid", "--require-handler", "44" ], output, new StringWriter(), signals, new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 1, exitCode );
		Assert.Equal( string.Empty, output.ToString() );
		Assert.Empty( signals.Deliveries );
	}

	/// <summary>Verifies --require-handler suppresses signals with no userspace handler.</summary>
	[Fact]
	public async Task RequireHandlerSkipsDefaultDisposition() {
		var signals = new FakeSignalProvider { Disposition = ProcessSignalDisposition.Default };
		var output = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "--verbose", "--require-handler", "44" ], output, new StringWriter(), signals, new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 1, exitCode );
		Assert.Empty( signals.Deliveries );
		Assert.Contains( "no userspace handler", output.ToString(), StringComparison.Ordinal );
	}

	/// <summary>Verifies queued data for a positive PID uses the shared F4 signal provider.</summary>
	[Fact]
	public async Task QueueUsesSharedSignalDelivery() {
		var platform = new FakePlatform();
		var signals = new FakeSignalProvider();
		var exitCode = await Command.RunAsync(
			[ "--queue", "17", "123" ], new StringWriter(), new StringWriter(), signals, new FakeInspector(), platform
		);
		Assert.Equal( 0, exitCode );
		var delivery = Assert.Single( signals.Deliveries );
		Assert.Equal( 123, delivery.Target.Identifier );
		Assert.Equal( 17, delivery.QueuedValue );
		Assert.Empty( platform.NativeDeliveries );
	}

	/// <summary>Verifies repeated timeout stages and queue data are kept on the pidfd path.</summary>
	[Fact]
	public async Task TimeoutSequenceUsesPidFdPath() {
		var platform = new FakePlatform();
		var output = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "--verbose", "--timeout", "10", "TERM", "--timeout", "20", "KILL", "--queue", "9", "-s", "QUIT", "123" ],
			output, new StringWriter(), new FakeSignalProvider(), new FakeInspector(), platform
		);
		Assert.Equal( 0, exitCode );
		var delivery = Assert.Single( platform.PidFdDeliveries );
		Assert.Equal( 123, delivery.ProcessId );
		Assert.Equal( 3, delivery.Signal.Number );
		Assert.Equal( 9, delivery.QueuedValue );
		Assert.Equal( [ 15, 9 ], delivery.Timeouts.Select( timeout => timeout.Signal.Number ).ToArray() );
		Assert.Contains( "timeout, sending signal 15", output.ToString(), StringComparison.Ordinal );
	}

	/// <summary>Verifies queued delivery takes util-linux precedence over pidfd-inode delivery when no timeout is requested.</summary>
	[Fact]
	public async Task QueueTakesPrecedenceOverPidFdInodeWithoutTimeout() {
		var platform = new FakePlatform();
		var signals = new FakeSignalProvider();
		var exitCode = await Command.RunAsync(
			[ "-TERM", "--queue", "9", "123:4567" ],
			new StringWriter(), new StringWriter(), signals, new FakeInspector(), platform
		);
		Assert.Equal( 0, exitCode );
		var delivery = Assert.Single( signals.Deliveries );
		Assert.Equal( 123, delivery.Target.Identifier );
		Assert.Equal( 9, delivery.QueuedValue );
		Assert.Empty( platform.NativeDeliveries );
		Assert.Empty( platform.PidFdDeliveries );
	}

	/// <summary>Verifies PID plus pidfd-inode syntax is forwarded to race-free delivery.</summary>
	[Fact]
	public async Task PidFdInodeIdentityIsPreserved() {
		var platform = new FakePlatform();
		var exitCode = await Command.RunAsync(
			[ "-TERM", "123:4567" ], new StringWriter(), new StringWriter(), new FakeSignalProvider(), new FakeInspector(), platform
		);
		Assert.Equal( 0, exitCode );
		Assert.Equal<ulong?>( 4567UL, Assert.Single( platform.PidFdDeliveries ).ExpectedInode );
	}

	/// <summary>Verifies pidfd-inode operands are accepted only with an explicit signal option.</summary>
	[Fact]
	public async Task PidFdInodeRequiresExplicitSignal() {
		var error = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "123:4567" ], new StringWriter(), error, new FakeSignalProvider(), new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 1, exitCode );
		Assert.Contains( "requires an explicit signal", error.ToString(), StringComparison.Ordinal );
	}

	/// <summary>Verifies race-free timeout delivery refuses targets that cannot be represented by a pidfd.</summary>
	[Fact]
	public async Task TimeoutRejectsNegativeProcessGroupTarget() {
		var error = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "-TERM", "--timeout", "1", "KILL", "-42" ],
			new StringWriter(), error, new FakeSignalProvider(), new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 1, exitCode );
		Assert.Contains( "requires a positive process identifier", error.ToString(), StringComparison.Ordinal );
	}

	/// <summary>Verifies full and failed targets produce util-linux partial-success status 64.</summary>
	[Fact]
	public async Task MixedDeliveryResultReturnsPartialSuccess() {
		var signals = new FakeSignalProvider { FailProcessId = 2 };
		var exitCode = await Command.RunAsync(
			[ "1", "2" ], new StringWriter(), new StringWriter(), signals, new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 64, exitCode );
	}

	/// <summary>Verifies Linux process-state masks are formatted as signal names.</summary>
	[Fact]
	public async Task ShowProcessStateDecodesMasks() {
		var platform = new FakePlatform {
			SignalStates = [ new KillSignalState( "Blocked", ( 1UL << 1 ) | ( 1UL << 14 ) ) ]
		};
		var output = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "-d", "77" ], output, new StringWriter(), new FakeSignalProvider(), new FakeInspector(), platform
		);
		Assert.Equal( 0, exitCode );
		Assert.Equal( string.Concat( "Blocked: INT TERM", Environment.NewLine ), output.ToString() );
	}

	/// <summary>Verifies the util-linux -v spelling remains a version alias rather than verbose mode.</summary>
	[Fact]
	public async Task LowercaseVIsVersionAlias() {
		var output = new StringWriter();
		var exitCode = await Command.RunAsync(
			[ "-v" ], output, new StringWriter(), new FakeSignalProvider(), new FakeInspector(), new FakePlatform()
		);
		Assert.Equal( 0, exitCode );
		Assert.Contains( "util-linux 2.42.2", output.ToString(), StringComparison.Ordinal );
	}

	private sealed class FakeInspector : IProcessInspector {
		public ProcessControlCapabilities Capabilities => ProcessControlCapabilities.ProcessIdentity | ProcessControlCapabilities.ReuseToken;
		public ProcessOperationResult<ProcessIdentity> ObserveIdentity( int processId ) => ProcessOperationResult<ProcessIdentity>.Success(
			new ProcessIdentity( processId, new ProcessReuseToken( "test", processId.ToString() ) )
		);
		public ProcessOperationResult<bool> ObserveLiveness( ProcessTarget target ) => ProcessOperationResult<bool>.Success( true );
		public Task<ProcessOperationResult<ProcessTermination>> WaitAsync( ProcessIdentity identity, CancellationToken cancellationToken = default ) =>
			Task.FromResult( ProcessOperationResult<ProcessTermination>.Success( ProcessTermination.Exited( 0 ) ) );
	}

	private sealed class FakeSignalProvider : IProcessSignalProvider {
		public ProcessControlCapabilities Capabilities => ProcessControlCapabilities.SignalDelivery | ProcessControlCapabilities.SignalDisposition | ProcessControlCapabilities.QueuedSignalDelivery;
		public List<( ProcessTarget Target, ProcessSignal Signal, int? QueuedValue )> Deliveries { get; } = [];
		public ProcessSignalDisposition Disposition { get; set; } = ProcessSignalDisposition.Caught;
		public int? FailProcessId { get; set; }
		public IReadOnlyList<ProcessSignal> ListSignals() => Enumerable.Range( 0, 32 )
			.Select( number => ProcessSignalCatalog.Translate( number ) )
			.Where( result => result.Succeeded )
			.Select( result => result.Value! )
			.ToArray();
		public ProcessOperationResult<ProcessSignal> ParseSignal( string text ) => ProcessSignalCatalog.Parse( text );
		public ProcessOperationResult<ProcessSignal> TranslateSignal( int number ) => ProcessSignalCatalog.Translate( number );
		public ProcessOperationResult<ProcessSignalDisposition> ObserveDisposition( ProcessIdentity identity, ProcessSignal signal ) =>
			ProcessOperationResult<ProcessSignalDisposition>.Success( this.Disposition );
		public Task<ProcessOperationResult> DeliverAsync(
			ProcessTarget target,
			ProcessSignal signal,
			int? queuedValue = null,
			CancellationToken cancellationToken = default
		) {
			this.Deliveries.Add( ( target, signal, queuedValue ) );
			return Task.FromResult(
				this.FailProcessId == target.Identifier
					? ProcessOperationResult.Failure( ProcessOperationStatus.AccessDenied, "denied" )
					: ProcessOperationResult.Success()
			);
		}
	}

	private sealed class FakePlatform : IKillPlatform {
		public bool SupportsRestrictedNameLookup => true;
		public Dictionary<string, IReadOnlyList<int>> Names { get; } = new( StringComparer.Ordinal );
		public bool LastAllUsers { get; private set; }
		public IReadOnlyList<KillSignalState> SignalStates { get; set; } = [];
		public List<( int ProcessId, ProcessSignal Signal, int? QueuedValue )> NativeDeliveries { get; } = [];
		public List<( int ProcessId, ulong? ExpectedInode, ProcessSignal Signal, int? QueuedValue, IReadOnlyList<KillTimeout> Timeouts )> PidFdDeliveries { get; } = [];
		public ProcessOperationResult<IReadOnlyList<int>> ResolveProcessName( string name, bool allUsers ) {
			this.LastAllUsers = allUsers;
			return ProcessOperationResult<IReadOnlyList<int>>.Success(
				this.Names.TryGetValue( name, out var values ) ? values : Array.Empty<int>()
			);
		}
		public ProcessOperationResult<IReadOnlyList<KillSignalState>> ReadSignalState( int processId ) =>
			ProcessOperationResult<IReadOnlyList<KillSignalState>>.Success( this.SignalStates );
		public Task<ProcessOperationResult> DeliverNativeTargetAsync(
			int nativeProcessId,
			ProcessSignal signal,
			int? queuedValue,
			CancellationToken cancellationToken = default
		) {
			this.NativeDeliveries.Add( ( nativeProcessId, signal, queuedValue ) );
			return Task.FromResult( ProcessOperationResult.Success() );
		}
		public Task<ProcessOperationResult> DeliverPidFdAsync(
			int processId,
			ulong? expectedPidFdInode,
			ProcessSignal signal,
			int? queuedValue,
			IReadOnlyList<KillTimeout> timeouts,
			Action<ProcessSignal>? timeoutSignalObserver = null,
			CancellationToken cancellationToken = default
		) {
			this.PidFdDeliveries.Add( ( processId, expectedPidFdInode, signal, queuedValue, timeouts.ToArray() ) );
			foreach ( var timeout in timeouts ) timeoutSignalObserver?.Invoke( timeout.Signal );
			return Task.FromResult( ProcessOperationResult.Success() );
		}
	}
}
