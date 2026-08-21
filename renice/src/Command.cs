namespace Icod.UtilLinux.Renice;

using System.Globalization;
using System.Text;
using Icod.CommandFramework.Platform;
using Icod.CommandFramework.Processes;

/// <summary>Implements the util-linux 2.42.2 <c>renice</c> command profile.</summary>
public static class Command {
	private static readonly Encoding Utf8 = new UTF8Encoding( false );

	/// <summary>Runs <c>renice</c> asynchronously with injectable F4 and identity providers.</summary>
	public static async Task<int> RunAsync(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcessPrioritySelectorProvider? priorityProvider = null,
		IIdentityProvider? identityProvider = null,
		ProcessEnvironment? sourceEnvironment = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		var priorities = priorityProvider ?? SystemProcessPrioritySelectorProvider.Instance;
		var identities = identityProvider ?? SystemIdentityProvider.Instance;
		var environment = sourceEnvironment ?? ProcessEnvironment.CreateInheritedBuilder().Build();

		if ( 1 == args.Length && args[ 0 ] is "-h" or "--help" ) {
			await WriteAsync( stdout, string.Concat( NormalizeLineEndings( HelpText ), Environment.NewLine ), cancellationToken ).ConfigureAwait( false );
			return 0;
		}
		if ( 1 == args.Length && args[ 0 ] is "-v" or "-V" or "--version" ) {
			await WriteAsync( stdout, string.Concat( "renice from util-linux 2.42.2 (Icod.UtilLinux)", Environment.NewLine ), cancellationToken ).ConfigureAwait( false );
			return 0;
		}

		var index = 0;
		var relative = false;
		if ( index < args.Length ) {
			if ( "-n" == args[ index ] ) {
				relative = environment.Variables.ContainsKey( "POSIXLY_CORRECT" );
				index++;
			} else if ( "--relative" == args[ index ] ) {
				relative = true;
				index++;
			} else if ( "--priority" == args[ index ] ) {
				index++;
			}
		}
		if ( args.Length - index < 2 ) {
			await WriteDiagnosticAsync( stderr, "renice: not enough arguments", cancellationToken ).ConfigureAwait( false );
			await WriteDiagnosticAsync( stderr, "Try 'renice --help' for more information.", cancellationToken ).ConfigureAwait( false );
			return 1;
		}
		if ( !TryParseSignedInt( args[ index ], out var requested ) ) {
			await WriteDiagnosticAsync( stderr, $"renice: invalid priority '{args[ index ]}'", cancellationToken ).ConfigureAwait( false );
			await WriteDiagnosticAsync( stderr, "Try 'renice --help' for more information.", cancellationToken ).ConfigureAwait( false );
			return 1;
		}
		index++;

		var kind = ProcessPriorityTargetKind.Process;
		var failed = false;
		for ( ; index < args.Length; index++ ) {
			var operand = args[ index ];
			if ( operand is "-p" or "--pid" ) {
				kind = ProcessPriorityTargetKind.Process;
				continue;
			}
			if ( operand is "-g" or "--pgrp" ) {
				kind = ProcessPriorityTargetKind.ProcessGroup;
				continue;
			}
			if ( operand is "-u" or "--user" ) {
				kind = ProcessPriorityTargetKind.User;
				continue;
			}

			var resolved = await ResolveTargetAsync( kind, operand, identities, cancellationToken ).ConfigureAwait( false );
			if ( !resolved.Succeeded ) {
				failed = true;
				await WriteDiagnosticAsync( stderr, $"renice: {resolved.Message}", cancellationToken ).ConfigureAwait( false );
				continue;
			}
			if ( !await ReniceTargetAsync( resolved.Value!, requested, relative, priorities, stdout, stderr, cancellationToken ).ConfigureAwait( false ) ) {
				failed = true;
			}
		}
		return failed ? 1 : 0;
	}

	/// <summary>Runs <c>renice</c> synchronously for compatibility with simple callers.</summary>
	public static int Run(
		string[] args,
		TextReader? stdin = null,
		TextWriter? stdout = null,
		TextWriter? stderr = null
	) {
		ArgumentNullException.ThrowIfNull( args );
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = RunAsync( args, output, error ).GetAwaiter().GetResult();
		( stdout ?? Console.Out ).Write( Utf8.GetString( output.ToArray() ) );
		( stderr ?? Console.Error ).Write( Utf8.GetString( error.ToArray() ) );
		return status;
	}

	private static async Task<bool> ReniceTargetAsync(
		ProcessPriorityTarget target,
		int requested,
		bool relative,
		IProcessPrioritySelectorProvider priorities,
		Stream? stdout,
		Stream? stderr,
		CancellationToken cancellationToken
	) {
		var label = TargetLabel( target.Kind );
		var current = priorities.GetPriority( target );
		if ( !current.Succeeded ) {
			await WriteOperationFailureAsync( stderr, "get", target, label, current.Message, cancellationToken ).ConfigureAwait( false );
			return false;
		}
		var newValue = relative
			? checked( (int)Math.Clamp( (long)current.Value!.NiceValue + requested, -20L, 19L ) )
			: Math.Clamp( requested, -20, 19 );
		var changed = priorities.SetPriority( target, newValue );
		if ( !changed.Succeeded ) {
			await WriteOperationFailureAsync( stderr, "set", target, label, changed.Message, cancellationToken ).ConfigureAwait( false );
			return false;
		}
		var observed = priorities.GetPriority( target );
		if ( !observed.Succeeded ) {
			await WriteOperationFailureAsync( stderr, "get", target, label, observed.Message, cancellationToken ).ConfigureAwait( false );
			return false;
		}
		await WriteAsync(
			stdout,
			string.Concat(
				target.Identifier.ToString( CultureInfo.InvariantCulture ),
				" (", label, ") old priority ",
				current.Value!.NiceValue.ToString( CultureInfo.InvariantCulture ),
				", new priority ",
				observed.Value!.NiceValue.ToString( CultureInfo.InvariantCulture ),
				Environment.NewLine
			),
			cancellationToken
		).ConfigureAwait( false );
		return true;
	}

	private static async ValueTask<ProcessOperationResult<ProcessPriorityTarget>> ResolveTargetAsync(
		ProcessPriorityTargetKind kind,
		string operand,
		IIdentityProvider identities,
		CancellationToken cancellationToken
	) {
		if ( ProcessPriorityTargetKind.User == kind ) {
			UserIdentity? user = null;
			try {
				user = await identities.FindUserAsync( operand, cancellationToken ).ConfigureAwait( false );
			} catch ( ArgumentException ) {
				// Numeric and otherwise non-name operands are handled below.
			}
			if ( null != user ) {
				if ( !TryParseNonNegativeInt( user.Id, out var userId ) ) {
					return ProcessOperationResult<ProcessPriorityTarget>.Failure(
						ProcessOperationStatus.Unsupported,
						$"user '{operand}' does not have a POSIX numeric user ID on this host"
					);
				}
				return ProcessOperationResult<ProcessPriorityTarget>.Success( ProcessPriorityTarget.ForUser( userId ) );
			}
			if ( !TryParseNonNegativeInt( operand, out var numericUserId ) ) {
				return ProcessOperationResult<ProcessPriorityTarget>.Failure(
					ProcessOperationStatus.InvalidArgument,
					$"unknown user {operand}"
				);
			}
			return ProcessOperationResult<ProcessPriorityTarget>.Success( ProcessPriorityTarget.ForUser( numericUserId ) );
		}

		if ( !TryParseNonNegativeInt( operand, out var identifier ) ) {
			return ProcessOperationResult<ProcessPriorityTarget>.Failure(
				ProcessOperationStatus.InvalidArgument,
				$"bad {TargetLabel( kind )} value: {operand}"
			);
		}
		return ProcessOperationResult<ProcessPriorityTarget>.Success(
			ProcessPriorityTargetKind.Process == kind
				? ProcessPriorityTarget.ForProcess( identifier )
				: ProcessPriorityTarget.ForProcessGroup( identifier )
		);
	}

	private static bool TryParseSignedInt( string text, out int value ) {
		if ( 0 == text.Length ) {
			value = 0;
			return true;
		}
		return int.TryParse(
			text,
			NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign,
			CultureInfo.InvariantCulture,
			out value
		);
	}

	private static bool TryParseNonNegativeInt( string text, out int value ) => TryParseSignedInt( text, out value ) && 0 <= value;

	private static string TargetLabel( ProcessPriorityTargetKind kind ) => kind switch {
		ProcessPriorityTargetKind.Process => "process ID",
		ProcessPriorityTargetKind.ProcessGroup => "process group ID",
		ProcessPriorityTargetKind.User => "user ID",
		_ => throw new ArgumentOutOfRangeException( nameof( kind ) )
	};

	private static async Task WriteOperationFailureAsync(
		Stream? stderr,
		string operation,
		ProcessPriorityTarget target,
		string label,
		string? detail,
		CancellationToken cancellationToken
	) => await WriteDiagnosticAsync(
		stderr,
		string.Concat(
			"renice: failed to ", operation, " priority for ",
			target.Identifier.ToString( CultureInfo.InvariantCulture ),
			" (", label, ")",
			null == detail ? string.Empty : $": {detail}"
		),
		cancellationToken
	).ConfigureAwait( false );

	private static async Task WriteAsync( Stream? stream, string text, CancellationToken cancellationToken ) {
		if ( null == stream ) {
			await Console.Out.WriteAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false );
			return;
		}
		var bytes = Utf8.GetBytes( text );
		await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
		await stream.FlushAsync( cancellationToken ).ConfigureAwait( false );
	}

	private static async Task WriteDiagnosticAsync( Stream? stream, string text, CancellationToken cancellationToken ) {
		if ( null == stream ) {
			await Console.Error.WriteLineAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false );
			return;
		}
		var bytes = Utf8.GetBytes( string.Concat( text, Environment.NewLine ) );
		await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
		await stream.FlushAsync( cancellationToken ).ConfigureAwait( false );
	}

	private static string NormalizeLineEndings( string value ) => "\n" == Environment.NewLine
		? value
		: value.Replace( "\n", Environment.NewLine, StringComparison.Ordinal );

	private const string HelpText = """
Usage:
 renice [-n|--priority|--relative] <priority> [-p|--pid] <pid>...
 renice [-n|--priority|--relative] <priority>  -g|--pgrp <pgid>...
 renice [-n|--priority|--relative] <priority>  -u|--user <user>...

Alter the priority of running processes.

 -n <num>               specify the 'absolute' nice value,
                        but 'relative' when POSIXLY_CORRECT is set
 --priority <num>       specify the 'absolute' nice value
 --relative <num>       specify the 'relative' nice value
 -p, --pid              interpret arguments as process ID (default)
 -g, --pgrp             interpret arguments as process group ID
 -u, --user             interpret arguments as username or user ID
 -h, --help             display this help
 -V, --version          display version
""";
}
