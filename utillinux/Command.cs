namespace Icod.UtilLinux.Router;

using System.Reflection;
using KillCommand = Icod.UtilLinux.Kill.Command;
using ReniceCommand = Icod.UtilLinux.Renice.Command;

/// <summary>Routes <c>utillinux</c> subcommands to their managed implementations.</summary>
public static class Command {
	private const int Success = 0;
	private const int UsageError = 2;

	private const string HelpText = """
Usage: utillinux COMMAND [ARG]...

Commands:
  kill      send signals to processes
  renice    alter priority of running processes

Options:
  -h, --help       display this help and exit
  -V, --version    output version information and exit
""";

	/// <summary>Runs the router.</summary>
	public static async Task<int> RunAsync(
		string[] args,
		TextWriter? stdout = null,
		TextWriter? stderr = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		stdout ??= Console.Out;
		stderr ??= Console.Error;
		cancellationToken.ThrowIfCancellationRequested();

		if ( 0 == args.Length ) {
			await stderr.WriteLineAsync( "utillinux: missing command" ).ConfigureAwait( false );
			await stderr.WriteLineAsync( "Try 'utillinux --help' for more information." ).ConfigureAwait( false );
			return UsageError;
		}

		if ( 1 == args.Length && args[ 0 ] is "-h" or "--help" ) {
			await stdout.WriteLineAsync( HelpText ).ConfigureAwait( false );
			return Success;
		}

		if ( 1 == args.Length && args[ 0 ] is "-V" or "--version" ) {
			await stdout.WriteLineAsync( string.Concat( "utillinux ", GetVersion() ) ).ConfigureAwait( false );
			return Success;
		}

		var commandArgs = args[ 1.. ];
		return args[ 0 ] switch {
			"kill" => await KillCommand.RunAsync(
				commandArgs,
				stdout,
				stderr,
				cancellationToken: cancellationToken
			).ConfigureAwait( false ),
			"renice" => RunRenice( commandArgs, stdout, stderr, cancellationToken ),
			_ => await UnknownCommandAsync( args[ 0 ], stderr ).ConfigureAwait( false )
		};
	}

	private static int RunRenice(
		string[] args,
		TextWriter stdout,
		TextWriter stderr,
		CancellationToken cancellationToken
	) {
		cancellationToken.ThrowIfCancellationRequested();
		return ReniceCommand.Run(
			args,
			stdout: stdout,
			stderr: stderr
		);
	}

	private static async Task<int> UnknownCommandAsync(
		string command,
		TextWriter stderr
	) {
		await stderr.WriteLineAsync(
			string.Concat( "utillinux: unknown command '", command, "'" )
		).ConfigureAwait( false );
		await stderr.WriteLineAsync( "Try 'utillinux --help' for more information." ).ConfigureAwait( false );
		return UsageError;
	}

	private static string GetVersion() {
		var assembly = typeof( Command ).Assembly;
		var informational = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
			.InformationalVersion;
		if ( !string.IsNullOrWhiteSpace( informational ) ) {
			var metadataIndex = informational.IndexOf( '+' );
			return 0 <= metadataIndex
				? informational[ ..metadataIndex ]
				: informational;
		}
		return assembly.GetName().Version?.ToString( 3 ) ?? "unknown";
	}
}
