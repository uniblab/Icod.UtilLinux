namespace Icod.UtilLinux.Kill;

using System.Globalization;
using Icod.CommandFramework.Processes;

/// <summary>Implements the util-linux 2.42.2 <c>kill</c> command profile.</summary>
public static class Command {
	private const int Success = 0;
	private const int Failure = 1;
	private const int PartialSuccess = 64;

	/// <summary>Runs <c>kill</c> using the supplied process-control providers.</summary>
	public static async Task<int> RunAsync(
		string[] args,
		TextWriter? stdout = null,
		TextWriter? stderr = null,
		IProcessSignalProvider? signalProvider = null,
		IProcessInspector? processInspector = null,
		IKillPlatform? platform = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		stdout ??= Console.Out;
		stderr ??= Console.Error;
		var signals = signalProvider ?? SystemProcessSignalProvider.Instance;
		var inspector = processInspector ?? SystemProcessInspector.Instance;
		var host = platform ?? SystemKillPlatform.Instance;

		var parsed = ParseArguments( args, signals );
		if ( !parsed.Succeeded ) {
			await WriteDiagnosticAsync( stderr, parsed.Error! ).ConfigureAwait( false );
			return Failure;
		}
		var options = parsed.Options!;
		if ( options.ShowHelp ) {
			await stdout.WriteAsync( NormalizeLineEndings( HelpText ) ).ConfigureAwait( false );
			return Success;
		}
		if ( options.ShowVersion ) {
			await stdout.WriteLineAsync( "kill from util-linux 2.42.2 (Icod.UtilLinux)" ).ConfigureAwait( false );
			return Success;
		}
		if ( options.ListSignals ) {
			return await RunListAsync( options.ListArgument, signals, stdout, stderr ).ConfigureAwait( false );
		}
		if ( options.ShowTable ) {
			await PrintSignalTableAsync( signals, stdout ).ConfigureAwait( false );
			return Success;
		}
		if ( null != options.ShowProcessState ) {
			return await RunShowStateAsync( options.ShowProcessState.Value, signals, host, stdout, stderr ).ConfigureAwait( false );
		}
		if ( 0 == options.Operands.Count ) {
			await WriteDiagnosticAsync( stderr, "kill: not enough arguments" ).ConfigureAwait( false );
			return Failure;
		}

		var attempted = 0;
		var failed = 0;
		foreach ( var operand in options.Operands ) {
			var targets = ResolveOperand( operand, options.AllUsers, host );
			if ( !targets.Succeeded ) {
				attempted++;
				failed++;
				await WriteDiagnosticAsync( stderr, $"kill: {targets.Message}" ).ConfigureAwait( false );
				continue;
			}
			if ( 0 == targets.Value!.Count ) {
				attempted++;
				failed++;
				await WriteDiagnosticAsync( stderr, $"kill: cannot find process \"{operand}\"" ).ConfigureAwait( false );
				continue;
			}
			foreach ( var target in targets.Value ) {
				if ( null != target.ExpectedPidFdInode && !options.SignalSpecified ) {
					attempted++;
					failed++;
					await WriteDiagnosticAsync( stderr, "kill: pid:pidfd_inode requires an explicit signal option" ).ConfigureAwait( false );
					continue;
				}
				if ( options.RequireHandler ) {
					var handler = HasRequiredHandler( target, options.Signal, inspector, signals );
					if ( !handler.Succeeded ) {
						attempted++;
						failed++;
						await WriteDiagnosticAsync( stderr, $"kill: {handler.Message}" ).ConfigureAwait( false );
						continue;
					}
					if ( !handler.Value ) {
						if ( options.Verbose ) {
							await stdout.WriteLineAsync(
								$"not signalling pid {target.NativeProcessId}, it has no userspace handler for signal {options.Signal.Number}"
							).ConfigureAwait( false );
						}
						continue;
					}
				}
				attempted++;
				if ( options.Verbose ) {
					await stdout.WriteLineAsync(
						$"sending signal {options.Signal.Number} to pid {target.NativeProcessId}"
					).ConfigureAwait( false );
				}
				if ( options.PidOnly ) {
					await stdout.WriteLineAsync( target.NativeProcessId.ToString( CultureInfo.InvariantCulture ) ).ConfigureAwait( false );
					continue;
				}
				ProcessOperationResult delivery;
				if ( 0 < target.NativeProcessId && 0 < options.Timeouts.Count ) {
					delivery = await host.DeliverPidFdAsync(
						target.NativeProcessId,
						target.ExpectedPidFdInode,
						options.Signal,
						options.QueuedValue,
						options.Timeouts,
						options.Verbose
							? timeoutSignal => stdout.WriteLine( $"timeout, sending signal {timeoutSignal.Number} to pid {target.NativeProcessId}" )
							: null,
						cancellationToken
					).ConfigureAwait( false );
				} else if ( 0 < options.Timeouts.Count ) {
					delivery = ProcessOperationResult.Failure(
						ProcessOperationStatus.Unsupported,
						"--timeout requires a positive process identifier so a Linux pidfd can protect against PID reuse."
					);
				} else if ( 0 < target.NativeProcessId && null != target.ExpectedPidFdInode && null == options.QueuedValue ) {
					delivery = await host.DeliverPidFdAsync(
						target.NativeProcessId,
						target.ExpectedPidFdInode,
						options.Signal,
						null,
						Array.Empty<KillTimeout>(),
						null,
						cancellationToken
					).ConfigureAwait( false );
				} else if ( -1 > target.NativeProcessId && null == options.QueuedValue ) {
					if ( int.MinValue == target.NativeProcessId ) {
						delivery = ProcessOperationResult.Failure(
							ProcessOperationStatus.InvalidArgument,
							$"invalid process-group target: {target.Operand}"
						);
					} else {
						delivery = await signals.DeliverAsync(
							ProcessTarget.ForProcessGroup( -target.NativeProcessId ),
							options.Signal,
							null,
							cancellationToken
						).ConfigureAwait( false );
					}
				} else if ( 0 >= target.NativeProcessId ) {
					delivery = await host.DeliverNativeTargetAsync(
						target.NativeProcessId,
						options.Signal,
						options.QueuedValue,
						cancellationToken
					).ConfigureAwait( false );
				} else {
					var identity = inspector.ObserveIdentity( target.NativeProcessId );
					if ( !identity.Succeeded ) {
						delivery = ProcessOperationResult.Failure( identity.Status, identity.Message, identity.NativeErrorCode );
					} else {
						delivery = await signals.DeliverAsync(
							ProcessTarget.ForProcess( identity.Value! ),
							options.Signal,
							options.QueuedValue,
							cancellationToken
						).ConfigureAwait( false );
					}
				}
				if ( !delivery.Succeeded ) {
					failed++;
					await WriteDiagnosticAsync(
						stderr,
						$"kill: sending signal to {target.Operand} failed: {delivery.Message ?? delivery.Status.ToString()}"
					).ConfigureAwait( false );
				}
			}
		}
		if ( 0 < attempted && 0 == failed ) return Success;
		if ( attempted == failed ) return Failure;
		return PartialSuccess;
	}

	/// <summary>Compatibility wrapper for callers of the historical synchronous command API.</summary>
	public static int Run(
		string[] args,
		TextReader? stdin = null,
		TextWriter? stdout = null,
		TextWriter? stderr = null
	) => RunAsync( args, stdout, stderr ).GetAwaiter().GetResult();

	private static ProcessOperationResult<bool> HasRequiredHandler(
		KillResolvedTarget target,
		ProcessSignal signal,
		IProcessInspector inspector,
		IProcessSignalProvider signals
	) {
		if ( 0 >= target.NativeProcessId || 0 >= signal.Number ) {
			return ProcessOperationResult<bool>.Failure(
				ProcessOperationStatus.Unsupported,
				"--require-handler requires a positive PID and a real signal."
			);
		}
		var identity = inspector.ObserveIdentity( target.NativeProcessId );
		if ( !identity.Succeeded ) {
			return ProcessOperationResult<bool>.Failure( identity.Status, identity.Message, identity.NativeErrorCode );
		}
		var disposition = signals.ObserveDisposition( identity.Value!, signal );
		if ( !disposition.Succeeded ) {
			return ProcessOperationResult<bool>.Failure( disposition.Status, disposition.Message, disposition.NativeErrorCode );
		}
		return ProcessOperationResult<bool>.Success( ProcessSignalDisposition.Caught == disposition.Value );
	}

	private static ProcessOperationResult<IReadOnlyList<KillResolvedTarget>> ResolveOperand(
		string operand,
		bool allUsers,
		IKillPlatform platform
	) {
		if ( TryParsePidOperand( operand, out var processId, out var inode, out var pidError ) ) {
			return ProcessOperationResult<IReadOnlyList<KillResolvedTarget>>.Success(
				[ new KillResolvedTarget( operand, processId, inode ) ]
			);
		}
		if ( null != pidError ) {
			return ProcessOperationResult<IReadOnlyList<KillResolvedTarget>>.Failure( ProcessOperationStatus.InvalidArgument, pidError );
		}
		var resolved = platform.ResolveProcessName( operand, allUsers );
		if ( !resolved.Succeeded ) {
			return ProcessOperationResult<IReadOnlyList<KillResolvedTarget>>.Failure( resolved.Status, resolved.Message, resolved.NativeErrorCode );
		}
		return ProcessOperationResult<IReadOnlyList<KillResolvedTarget>>.Success(
			resolved.Value!.Select( process => new KillResolvedTarget( operand, process ) ).ToArray()
		);
	}

	private static bool TryParsePidOperand(
		string operand,
		out int processId,
		out ulong? inode,
		out string? error
	) {
		processId = 0;
		inode = null;
		error = null;
		var colon = operand.IndexOf( ':' );
		var pidText = 0 <= colon ? operand[ ..colon ] : operand;
		if ( !int.TryParse( pidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out processId ) ) return false;
		if ( 0 > colon ) return true;
		if ( 0 >= processId || colon == operand.Length - 1 || !ulong.TryParse(
			operand[ ( colon + 1 ).. ], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedInode
		) || 0 == parsedInode ) {
			error = $"invalid PID argument: {operand}";
			return false;
		}
		inode = parsedInode;
		return true;
	}

	private static async Task<int> RunListAsync(
		string? argument,
		IProcessSignalProvider signals,
		TextWriter stdout,
		TextWriter stderr
	) {
		if ( null == argument ) {
			foreach ( var signal in ListDisplaySignals( signals ) ) await stdout.WriteLineAsync( signal.Name ).ConfigureAwait( false );
			if ( OperatingSystem.IsLinux() ) {
				await stdout.WriteLineAsync( "RT<N>" ).ConfigureAwait( false );
				await stdout.WriteLineAsync( "RTMIN+<N>" ).ConfigureAwait( false );
				await stdout.WriteLineAsync( "RTMAX-<N>" ).ConfigureAwait( false );
			}
			return Success;
		}
		if ( argument.StartsWith( "0x", StringComparison.OrdinalIgnoreCase ) ) {
			if ( !ulong.TryParse( argument[ 2.. ], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var mask ) ) {
				await WriteDiagnosticAsync( stderr, $"kill: invalid sigmask format: {argument}" ).ConfigureAwait( false );
				return Failure;
			}
			await PrintSignalMaskAsync( mask, signals, stdout ).ConfigureAwait( false );
			return Success;
		}
		var parsed = ParseSignal( argument, signals, allowShellStatus: true );
		if ( !parsed.Succeeded ) {
			await WriteDiagnosticAsync( stderr, $"kill: unknown signal: {argument}" ).ConfigureAwait( false );
			return Failure;
		}
		await stdout.WriteLineAsync( DisplaySignalName( parsed.Value! ) ).ConfigureAwait( false );
		return Success;
	}

	private static async Task PrintSignalTableAsync(
		IProcessSignalProvider signals,
		TextWriter stdout
	) {
		foreach ( var signal in ListDisplaySignals( signals ) ) {
			await stdout.WriteLineAsync( $"{signal.Number,2} {signal.Name,-8}" ).ConfigureAwait( false );
		}
		if ( OperatingSystem.IsLinux() ) {
			var minimum = signals.TranslateSignal( 34 );
			var maximum = signals.TranslateSignal( 64 );
			if ( minimum.Succeeded ) await stdout.WriteLineAsync( $"{minimum.Value!.Number,2} {"RTMIN",-8}" ).ConfigureAwait( false );
			if ( maximum.Succeeded ) await stdout.WriteLineAsync( $"{maximum.Value!.Number,2} {"RTMAX",-8}" ).ConfigureAwait( false );
		}
	}

	private static IReadOnlyList<ProcessSignal> ListDisplaySignals(
		IProcessSignalProvider signals
	) {
		var canonical = signals.ListSignals()
			.Where( signal => 0 < signal.Number && ( !OperatingSystem.IsLinux() || 34 > signal.Number ) )
			.GroupBy( signal => signal.Number )
			.Select( group => group.First() )
			.OrderBy( signal => signal.Number )
			.ToArray()
		;
		if ( !OperatingSystem.IsLinux() ) return canonical;

		var result = new List<ProcessSignal>( canonical.Length + 3 );
		foreach ( var signal in canonical ) {
			result.Add( signal );
			switch ( signal.Number ) {
				case 6:
					result.Add( new ProcessSignal( 6, "IOT" ) );
					break;
				case 17:
					result.Add( new ProcessSignal( 17, "CLD" ) );
					break;
				case 29:
					result.Add( new ProcessSignal( 29, "POLL" ) );
					break;
			}
		}
		return result;
	}

	private static async Task<int> RunShowStateAsync(
		int processId,
		IProcessSignalProvider signals,
		IKillPlatform platform,
		TextWriter stdout,
		TextWriter stderr
	) {
		var state = platform.ReadSignalState( processId );
		if ( !state.Succeeded ) {
			await WriteDiagnosticAsync( stderr, $"kill: {state.Message}" ).ConfigureAwait( false );
			return Failure;
		}
		foreach ( var field in state.Value! ) {
			await stdout.WriteAsync( $"{field.Label}:" ).ConfigureAwait( false );
			for ( var bit = 0; bit < 64; bit++ ) {
				if ( 0 == ( field.Mask & ( 1UL << bit ) ) ) continue;
				var translated = signals.TranslateSignal( bit + 1 );
				var name = translated.Succeeded ? DisplaySignalName( translated.Value! ) : ( bit + 1 ).ToString( CultureInfo.InvariantCulture );
				await stdout.WriteAsync( $" {name}" ).ConfigureAwait( false );
			}
			await stdout.WriteLineAsync().ConfigureAwait( false );
		}
		return Success;
	}

	private static async Task PrintSignalMaskAsync(
		ulong mask,
		IProcessSignalProvider signals,
		TextWriter stdout
	) {
		for ( var bit = 0; bit < 64; bit++ ) {
			if ( 0 == ( mask & ( 1UL << bit ) ) ) continue;
			var translated = signals.TranslateSignal( bit + 1 );
			await stdout.WriteLineAsync(
				translated.Succeeded ? DisplaySignalName( translated.Value! ) : ( bit + 1 ).ToString( CultureInfo.InvariantCulture )
			).ConfigureAwait( false );
		}
	}

	private static ProcessOperationResult<ProcessSignal> ParseSignal(
		string text,
		IProcessSignalProvider signals,
		bool allowShellStatus = false
	) {
		if ( string.IsNullOrEmpty( text ) || !string.Equals( text, text.Trim(), StringComparison.Ordinal ) ) {
			return ProcessOperationResult<ProcessSignal>.Failure(
				ProcessOperationStatus.InvalidArgument,
				$"Unknown signal '{text}'."
			);
		}
		if ( '+' == text[ 0 ] || '-' == text[ 0 ] ) {
			return ProcessOperationResult<ProcessSignal>.Failure(
				ProcessOperationStatus.InvalidArgument,
				$"Unknown signal '{text}'."
			);
		}
		var normalized = text;
		if ( char.IsAsciiDigit( text[ 0 ] ) ) {
			if ( !int.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out var number ) ) {
				return ProcessOperationResult<ProcessSignal>.Failure(
					ProcessOperationStatus.InvalidArgument,
					$"Unknown signal '{text}'."
				);
			}
			if ( allowShellStatus && 128 <= number ) number -= 128;
			return signals.TranslateSignal( number );
		}
		var withoutPrefix = normalized.StartsWith( "SIG", StringComparison.OrdinalIgnoreCase ) ? normalized[ 3.. ] : normalized;
		if ( withoutPrefix.Equals( "RTMIN", StringComparison.OrdinalIgnoreCase )
			|| withoutPrefix.Equals( "RTMAX", StringComparison.OrdinalIgnoreCase ) ) {
			return ProcessOperationResult<ProcessSignal>.Failure(
				ProcessOperationStatus.InvalidArgument,
				$"Unknown signal '{text}'."
			);
		}
		if ( OperatingSystem.IsLinux()
			&& withoutPrefix.StartsWith( "RT", StringComparison.OrdinalIgnoreCase )
			&& !withoutPrefix.StartsWith( "RTMIN", StringComparison.OrdinalIgnoreCase )
			&& !withoutPrefix.StartsWith( "RTMAX", StringComparison.OrdinalIgnoreCase )
			&& int.TryParse( withoutPrefix[ 2.. ], NumberStyles.None, CultureInfo.InvariantCulture, out var realtimeOffset )
			&& 0 <= realtimeOffset
			&& 30 >= realtimeOffset ) {
			return signals.TranslateSignal( 34 + realtimeOffset );
		}
		return signals.ParseSignal( normalized );
	}

	private static string DisplaySignalName(
		ProcessSignal signal
	) {
		if ( OperatingSystem.IsLinux() && 34 <= signal.Number && 64 >= signal.Number ) {
			return $"RT{signal.Number - 34}";
		}
		return signal.Name;
	}

	private static ParseResult ParseArguments(
		string[] args,
		IProcessSignalProvider signals
	) {
		var term = signals.ParseSignal( "TERM" );
		if ( !term.Succeeded ) return ParseResult.ErrorResult( term.Message ?? "kill: TERM is unavailable" );
		var options = new KillOptions { Signal = term.Value! };
		var index = 0;
		for ( ; index < args.Length; index++ ) {
			var token = args[ index ];
			if ( !token.StartsWith( '-' ) || "-" == token ) break;
			if ( "--" == token ) {
				index++;
				break;
			}
			if ( token is "-h" or "--help" ) {
				options.ShowHelp = true;
				return ParseResult.SuccessResult( options );
			}
			if ( token is "-v" or "-V" or "--version" ) {
				options.ShowVersion = true;
				return ParseResult.SuccessResult( options );
			}
			if ( "--verbose" == token ) {
				options.Verbose = true;
				continue;
			}
			if ( token is "-a" or "--all" ) {
				options.AllUsers = true;
				continue;
			}
			if ( token is "-r" or "--require-handler" ) {
				options.RequireHandler = true;
				continue;
			}
			if ( token is "-p" or "--pid" ) {
				if ( options.SignalSpecified ) return ParseResult.ErrorResult( "kill: --pid and --signal are mutually exclusive" );
				if ( null != options.QueuedValue ) return ParseResult.ErrorResult( "kill: --pid and --queue are mutually exclusive" );
				options.PidOnly = true;
				continue;
			}
			if ( token is "-l" or "--list" ) {
				options.ListSignals = true;
				var remaining = args.Length - index - 1;
				if ( 1 < remaining ) return ParseResult.ErrorResult( "kill: too many arguments" );
				if ( 1 == remaining ) options.ListArgument = args[ index + 1 ];
				return ParseResult.SuccessResult( options );
			}
			if ( token.StartsWith( "--list=", StringComparison.Ordinal ) || token.StartsWith( "-l=", StringComparison.Ordinal ) ) {
				options.ListSignals = true;
				options.ListArgument = token[ ( token.IndexOf( '=' ) + 1 ).. ];
				return ParseResult.SuccessResult( options );
			}
			if ( token is "-L" or "--table" ) {
				options.ShowTable = true;
				return ParseResult.SuccessResult( options );
			}
			if ( token is "-d" or "--show-process-state" ) {
				if ( index + 1 >= args.Length ) return ParseResult.ErrorResult( "kill: too few arguments" );
				if ( index + 2 != args.Length ) return ParseResult.ErrorResult( "kill: too many arguments" );
				if ( !int.TryParse( args[ index + 1 ], NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId ) || 0 >= processId ) {
					return ParseResult.ErrorResult( $"kill: invalid PID argument: {args[ index + 1 ]}" );
				}
				options.ShowProcessState = processId;
				return ParseResult.SuccessResult( options );
			}
			if ( token.StartsWith( "-d=", StringComparison.Ordinal ) || token.StartsWith( "--show-process-state=", StringComparison.Ordinal ) ) {
				var value = token[ ( token.IndexOf( '=' ) + 1 ).. ];
				if ( !int.TryParse( value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId ) || 0 >= processId ) {
					return ParseResult.ErrorResult( $"kill: invalid PID argument: {value}" );
				}
				options.ShowProcessState = processId;
				return ParseResult.SuccessResult( options );
			}
			if ( token is "-s" or "--signal" ) {
				if ( options.PidOnly ) return ParseResult.ErrorResult( "kill: --pid and --signal are mutually exclusive" );
				if ( index + 1 >= args.Length ) return ParseResult.ErrorResult( "kill: not enough arguments" );
				var parsedSignal = ParseSignal( args[ ++index ], signals );
				if ( !parsedSignal.Succeeded ) return ParseResult.ErrorResult( $"kill: unknown signal: {args[ index ]}" );
				options.Signal = parsedSignal.Value!;
				options.SignalSpecified = true;
				continue;
			}
			if ( token is "-q" or "--queue" ) {
				if ( options.PidOnly ) return ParseResult.ErrorResult( "kill: --pid and --queue are mutually exclusive" );
				if ( index + 1 >= args.Length ) return ParseResult.ErrorResult( $"kill: option '{token}' requires an argument" );
				if ( !int.TryParse( args[ ++index ], NumberStyles.Integer, CultureInfo.InvariantCulture, out var queuedValue ) ) {
					return ParseResult.ErrorResult( $"kill: argument error: {args[ index ]}" );
				}
				options.QueuedValue = queuedValue;
				continue;
			}
			if ( "--timeout" == token ) {
				if ( index + 2 >= args.Length ) return ParseResult.ErrorResult( "kill: option '--timeout' requires two arguments" );
				if ( !int.TryParse( args[ ++index ], NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds ) || 0 > milliseconds ) {
					return ParseResult.ErrorResult( $"kill: argument error: {args[ index ]}" );
				}
				var followUpText = args[ ++index ];
				var followUp = ParseSignal( followUpText, signals );
				if ( !followUp.Succeeded ) return ParseResult.ErrorResult( $"kill: unknown signal: {followUpText}" );
				options.Timeouts.Add( new KillTimeout( milliseconds, followUp.Value! ) );
				continue;
			}
			if ( options.SignalSpecified ) break;
			var shortSignal = token[ 1.. ];
			var parsedShort = ParseSignal( shortSignal, signals );
			if ( !parsedShort.Succeeded ) return ParseResult.ErrorResult( $"kill: invalid signal name or number: {shortSignal}" );
			if ( options.PidOnly ) return ParseResult.ErrorResult( "kill: --pid and --signal are mutually exclusive" );
			options.Signal = parsedShort.Value!;
			options.SignalSpecified = true;
		}
		for ( ; index < args.Length; index++ ) options.Operands.Add( args[ index ] );
		return ParseResult.SuccessResult( options );
	}

	private static Task WriteDiagnosticAsync(
		TextWriter stderr,
		string message
	) => stderr.WriteLineAsync( message );

	private static string NormalizeLineEndings(
		string value
	) => "\n" == Environment.NewLine ? value : value.Replace( "\n", Environment.NewLine, StringComparison.Ordinal );

	private sealed class KillOptions {
		/// <summary>Gets or sets whether name lookup may match processes owned by other users.</summary>
		public bool AllUsers { get; set; }
		/// <summary>Gets or sets whether signal names are listed or translated.</summary>
		public bool ListSignals { get; set; }
		/// <summary>Gets or sets the optional signal or hexadecimal mask supplied to <c>--list</c>.</summary>
		public string? ListArgument { get; set; }
		/// <summary>Gets or sets whether resolved PIDs are printed without signal delivery.</summary>
		public bool PidOnly { get; set; }
		/// <summary>Gets or sets the optional queued integer payload.</summary>
		public int? QueuedValue { get; set; }
		/// <summary>Gets or sets whether a userspace handler is required before an operation is attempted.</summary>
		public bool RequireHandler { get; set; }
		/// <summary>Gets or sets the initial signal.</summary>
		public required ProcessSignal Signal { get; set; }
		/// <summary>Gets or sets whether the initial signal was supplied explicitly.</summary>
		public bool SignalSpecified { get; set; }
		/// <summary>Gets or sets whether help output is requested.</summary>
		public bool ShowHelp { get; set; }
		/// <summary>Gets or sets the PID whose Linux signal state should be displayed.</summary>
		public int? ShowProcessState { get; set; }
		/// <summary>Gets or sets whether the signal number/name table is requested.</summary>
		public bool ShowTable { get; set; }
		/// <summary>Gets or sets whether version output is requested.</summary>
		public bool ShowVersion { get; set; }
		/// <summary>Gets or sets whether prospective deliveries are reported.</summary>
		public bool Verbose { get; set; }
		/// <summary>Gets the ordered pidfd follow-up stages.</summary>
		public List<KillTimeout> Timeouts { get; } = [];
		/// <summary>Gets the process operands remaining after option parsing.</summary>
		public List<string> Operands { get; } = [];
	}

	private sealed class ParseResult {
		/// <summary>Gets the parsing diagnostic on failure.</summary>
		public string? Error { get; init; }
		/// <summary>Gets parsed options on success.</summary>
		public KillOptions? Options { get; init; }
		/// <summary>Gets whether parsing succeeded.</summary>
		public bool Succeeded => null != this.Options;
		/// <summary>Creates a failed parsing result.</summary>
		public static ParseResult ErrorResult( string error ) => new() { Error = error };
		/// <summary>Creates a successful parsing result.</summary>
		public static ParseResult SuccessResult( KillOptions options ) => new() { Options = options };
	}

	private const string HelpText = """
Usage:
 kill [options] <pid>|<pid>:<pidfd_inode>|<name>...

Forcibly terminate a process.

Options:
 -a, --all              do not restrict name-to-pid conversion to the same uid
 -s, --signal <signal>  send this signal instead of SIGTERM
 -q, --queue <value>    use sigqueue and pass an integer value
     --timeout <milliseconds> <signal>
                        wait and send a race-free pidfd follow-up signal
 -p, --pid              print pids without signaling them
 -l, --list[=<signal>|=0x<sigmask>]
                        list or translate signals
 -L, --table            list signal names and numbers
 -r, --require-handler  signal only when a userspace handler is installed
 -d, --show-process-state <pid>
                        show signal-related fields from /proc/PID/status
     --verbose           print pids that will be signaled
 -h, --help              display this help
 -V, --version           display version information
""";
}
