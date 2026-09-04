namespace Icod.UtilLinux.Router.Tests;

using Icod.UtilLinux.Router;
using Xunit;

public sealed class CommandTests {
	[Fact]
	public async Task HelpListsCommands() {
		var output = new StringWriter();
		var error = new StringWriter();

		var status = await Command.RunAsync(
			[ "--help" ],
			output,
			error
		);

		Assert.Equal( 0, status );
		Assert.Contains( "kill", output.ToString(), StringComparison.Ordinal );
		Assert.Contains( "renice", output.ToString(), StringComparison.Ordinal );
		Assert.Equal( string.Empty, error.ToString() );
	}

	[Fact]
	public async Task MissingCommandIsUsageError() {
		var output = new StringWriter();
		var error = new StringWriter();

		var status = await Command.RunAsync(
			[],
			output,
			error
		);

		Assert.Equal( 2, status );
		Assert.Contains( "missing command", error.ToString(), StringComparison.Ordinal );
	}

	[Fact]
	public async Task UnknownCommandIsUsageError() {
		var output = new StringWriter();
		var error = new StringWriter();

		var status = await Command.RunAsync(
			[ "unknown" ],
			output,
			error
		);

		Assert.Equal( 2, status );
		Assert.Contains( "unknown command", error.ToString(), StringComparison.Ordinal );
	}

	[Fact]
	public async Task VersionUsesCentralizedRepositoryVersion() {
		var output = new StringWriter();

		var status = await Command.RunAsync(
			[ "--version" ],
			output,
			new StringWriter()
		);

		Assert.Equal( 0, status );
		Assert.Contains( "utillinux 1.0.1", output.ToString(), StringComparison.Ordinal );
	}

	[Theory]
	[InlineData( "kill", "kill from util-linux" )]
	[InlineData( "renice", "renice from util-linux" )]
	public async Task DispatchesVersionToCommand(
		string command,
		string expected
	) {
		var output = new StringWriter();
		var error = new StringWriter();

		var status = await Command.RunAsync(
			[ command, "-V" ],
			output,
			error
		);

		Assert.Equal( 0, status );
		Assert.Contains( expected, output.ToString(), StringComparison.Ordinal );
		Assert.Equal( string.Empty, error.ToString() );
	}
}
