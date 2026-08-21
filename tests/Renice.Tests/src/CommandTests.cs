namespace Icod.UtilLinux.Renice.Tests;

using System.Text;
using Icod.CommandFramework.Platform;
using Icod.CommandFramework.Processes;
using PlatformProcessIdentity = Icod.CommandFramework.Platform.ProcessIdentity;
using Xunit;

/// <summary>Exercises util-linux 2.42.2 <c>renice</c> parsing and target semantics.</summary>
public sealed class CommandTests {
	/// <summary>Verifies ordered process, group, user, and process target changes.</summary>
	[Fact]
	public async Task AppliesOrderedTargetClasses() {
		var priorities = new FakePriorityProvider();
		var identities = new FakeIdentityProvider();
		identities.Users[ "daemon" ] = CreateUser( 42, "daemon" );
		using var output = new MemoryStream();
		var status = await Command.RunAsync(
			new[] { "5", "100", "-g", "200", "-u", "daemon", "-p", "300" },
			output,
			priorityProvider: priorities,
			identityProvider: identities,
			sourceEnvironment: ProcessEnvironment.CreateEmptyBuilder().Build()
		);
		Assert.Equal( 0, status );
		Assert.Equal(
			new[] {
				( ProcessPriorityTargetKind.Process, 100 ),
				( ProcessPriorityTargetKind.ProcessGroup, 200 ),
				( ProcessPriorityTargetKind.User, 42 ),
				( ProcessPriorityTargetKind.Process, 300 )
			},
			priorities.SetTargets.Select( item => ( item.Kind, item.Identifier ) ).ToArray()
		);
		Assert.All( priorities.SetValues, value => Assert.Equal( 5, value ) );
	}

	/// <summary>Verifies <c>-n</c> is absolute unless POSIXLY_CORRECT exists.</summary>
	[Theory]
	[InlineData( false, 3 )]
	[InlineData( true, 10 )]
	public async Task ShortNRespectsPosixlyCorrect( bool posixlyCorrect, int expected ) {
		var priorities = new FakePriorityProvider { DefaultPriority = 7 };
		var environment = ProcessEnvironment.CreateEmptyBuilder();
		if ( posixlyCorrect ) environment.Set( "POSIXLY_CORRECT", string.Empty );
		var status = await Command.RunAsync(
			new[] { "-n", "3", "123" },
			priorityProvider: priorities,
			identityProvider: new FakeIdentityProvider(),
			sourceEnvironment: environment.Build()
		);
		Assert.Equal( 0, status );
		Assert.Equal( expected, Assert.Single( priorities.SetValues ) );
	}

	/// <summary>Verifies explicit relative mode is independent of POSIXLY_CORRECT.</summary>
	[Fact]
	public async Task RelativeOptionAddsToCurrentPriority() {
		var priorities = new FakePriorityProvider { DefaultPriority = -2 };
		var status = await Command.RunAsync(
			new[] { "--relative", "4", "123" },
			priorityProvider: priorities,
			identityProvider: new FakeIdentityProvider(),
			sourceEnvironment: ProcessEnvironment.CreateEmptyBuilder().Build()
		);
		Assert.Equal( 0, status );
		Assert.Equal( 2, Assert.Single( priorities.SetValues ) );
	}

	/// <summary>Verifies username lookup takes precedence over numeric parsing.</summary>
	[Fact]
	public async Task ResolvesUsernameBeforeNumericUserId() {
		var identities = new FakeIdentityProvider();
		identities.Users[ "123" ] = CreateUser( 7, "123" );
		var priorities = new FakePriorityProvider();
		var status = await Command.RunAsync(
			new[] { "1", "-u", "123" },
			priorityProvider: priorities,
			identityProvider: identities,
			sourceEnvironment: ProcessEnvironment.CreateEmptyBuilder().Build()
		);
		Assert.Equal( 0, status );
		Assert.Equal( 7, Assert.Single( priorities.SetTargets ).Identifier );
	}

	/// <summary>Verifies selector zero is preserved for POSIX current-target semantics.</summary>
	[Theory]
	[InlineData( "-p", ProcessPriorityTargetKind.Process )]
	[InlineData( "-g", ProcessPriorityTargetKind.ProcessGroup )]
	[InlineData( "-u", ProcessPriorityTargetKind.User )]
	public async Task PreservesZeroSelectors( string selector, ProcessPriorityTargetKind expectedKind ) {
		var priorities = new FakePriorityProvider();
		var status = await Command.RunAsync(
			new[] { "8", selector, "0" },
			priorityProvider: priorities,
			identityProvider: new FakeIdentityProvider(),
			sourceEnvironment: ProcessEnvironment.CreateEmptyBuilder().Build()
		);
		Assert.Equal( 0, status );
		var target = Assert.Single( priorities.SetTargets );
		Assert.Equal( expectedKind, target.Kind );
		Assert.Equal( 0, target.Identifier );
	}

	/// <summary>Verifies one failed target does not prevent later targets from being attempted.</summary>
	[Fact]
	public async Task PartialFailureReturnsOneAndContinues() {
		var priorities = new FakePriorityProvider();
		priorities.FailGetIdentifiers.Add( 100 );
		using var error = new MemoryStream();
		var status = await Command.RunAsync(
			new[] { "5", "100", "200" },
			stderr: error,
			priorityProvider: priorities,
			identityProvider: new FakeIdentityProvider(),
			sourceEnvironment: ProcessEnvironment.CreateEmptyBuilder().Build()
		);
		Assert.Equal( 1, status );
		Assert.Contains( priorities.SetTargets, target => 200 == target.Identifier );
		Assert.Contains( "failed to get priority for 100", Encoding.UTF8.GetString( error.ToArray() ), StringComparison.Ordinal );
	}

	/// <summary>Verifies util-linux numeric target parsing follows signed <c>strtol</c> forms.</summary>
	[Theory]
	[InlineData( "+123", 123 )]
	[InlineData( "-0", 0 )]
	public async Task AcceptsSignedNonnegativeTargetForms( string operand, int expectedIdentifier ) {
		var priorities = new FakePriorityProvider();
		var status = await Command.RunAsync(
			new[] { "1", operand },
			priorityProvider: priorities,
			identityProvider: new FakeIdentityProvider(),
			sourceEnvironment: ProcessEnvironment.CreateEmptyBuilder().Build()
		);
		Assert.Equal( 0, status );
		Assert.Equal( expectedIdentifier, Assert.Single( priorities.SetTargets ).Identifier );
	}

	/// <summary>Verifies the util-linux <c>strtol</c> empty-operand quirk maps empty numeric text to zero.</summary>
	[Theory]
	[InlineData( true )]
	[InlineData( false )]
	public async Task EmptyNumericOperandMapsToZero( bool emptyPriority ) {
		var priorities = new FakePriorityProvider();
		var args = emptyPriority ? new[] { string.Empty, "123" } : new[] { "0", string.Empty };
		var status = await Command.RunAsync(
			args,
			priorityProvider: priorities,
			identityProvider: new FakeIdentityProvider(),
			sourceEnvironment: ProcessEnvironment.CreateEmptyBuilder().Build()
		);
		Assert.Equal( 0, status );
		if ( emptyPriority ) Assert.Equal( 0, Assert.Single( priorities.SetValues ) );
		else Assert.Equal( 0, Assert.Single( priorities.SetTargets ).Identifier );
	}

	/// <summary>Verifies explicit absolute mode wins even when POSIXLY_CORRECT is present.</summary>
	[Fact]
	public async Task PriorityOptionRemainsAbsoluteWithPosixlyCorrect() {
		var priorities = new FakePriorityProvider { DefaultPriority = 7 };
		var environment = ProcessEnvironment.CreateEmptyBuilder();
		environment.Set( "POSIXLY_CORRECT", string.Empty );
		var status = await Command.RunAsync(
			new[] { "--priority", "3", "123" },
			priorityProvider: priorities,
			identityProvider: new FakeIdentityProvider(),
			sourceEnvironment: environment.Build()
		);
		Assert.Equal( 0, status );
		Assert.Equal( 3, Assert.Single( priorities.SetValues ) );
	}

	/// <summary>Verifies absolute values are clamped to the host nice-value range.</summary>
	[Theory]
	[InlineData( "99", 19 )]
	[InlineData( "-99", -20 )]
	public async Task ClampsAbsolutePriority( string requested, int expected ) {
		var priorities = new FakePriorityProvider();
		var status = await Command.RunAsync(
			new[] { requested, "123" },
			priorityProvider: priorities,
			identityProvider: new FakeIdentityProvider(),
			sourceEnvironment: ProcessEnvironment.CreateEmptyBuilder().Build()
		);
		Assert.Equal( 0, status );
		Assert.Equal( expected, Assert.Single( priorities.SetValues ) );
	}

	private static UserIdentity CreateUser( int id, string name ) {
		var group = new GroupIdentity( id.ToString(), name );
		return new UserIdentity( id.ToString(), name, group, new[] { group } );
	}

	private sealed class FakeIdentityProvider : IIdentityProvider {
		internal Dictionary<string, UserIdentity> Users { get; } = new( StringComparer.Ordinal );
		public ValueTask<PlatformProcessIdentity> GetCurrentAsync( CancellationToken cancellationToken = default ) => throw new NotSupportedException();
		public ValueTask<UserIdentity?> FindUserAsync( string userName, CancellationToken cancellationToken = default ) => ValueTask.FromResult<UserIdentity?>( this.Users.TryGetValue( userName, out var user ) ? user : null );
		public ValueTask<UserIdentity?> FindUserByIdAsync( string userId, CancellationToken cancellationToken = default ) => ValueTask.FromResult<UserIdentity?>( null );
		public ValueTask<string?> GetLoginNameAsync( CancellationToken cancellationToken = default ) => ValueTask.FromResult<string?>( null );
	}

	private sealed class FakePriorityProvider : IProcessPrioritySelectorProvider {
		private readonly Dictionary<(ProcessPriorityTargetKind Kind, int Id), int> _values = new();
		internal int DefaultPriority { get; set; }
		internal HashSet<int> FailGetIdentifiers { get; } = new();
		internal List<ProcessPriorityTarget> SetTargets { get; } = new();
		internal List<int> SetValues { get; } = new();
		public ProcessControlCapabilities Capabilities => ProcessControlCapabilities.PriorityRead | ProcessControlCapabilities.PriorityWrite | ProcessControlCapabilities.ProcessGroupTargets | ProcessControlCapabilities.UserPriorityTargets;
		public ProcessOperationResult<ProcessPriorityValue> GetPriority( ProcessTarget target ) => ProcessOperationResult<ProcessPriorityValue>.Success( new ProcessPriorityValue( this.DefaultPriority, false ) );
		public ProcessOperationResult SetPriority( ProcessTarget target, int niceValue ) => ProcessOperationResult.Success();
		public ProcessOperationResult<ProcessPriorityValue> AdjustPriority( ProcessTarget target, int increment ) => ProcessOperationResult<ProcessPriorityValue>.Success( new ProcessPriorityValue( Math.Clamp( this.DefaultPriority + increment, -20, 19 ), false ) );
		public ProcessOperationResult<ProcessPriorityValue> GetPriority( ProcessPriorityTarget target ) {
			if ( this.FailGetIdentifiers.Contains( target.Identifier ) ) return ProcessOperationResult<ProcessPriorityValue>.Failure( ProcessOperationStatus.Vanished, "gone" );
			var value = this._values.TryGetValue( ( target.Kind, target.Identifier ), out var stored ) ? stored : this.DefaultPriority;
			return ProcessOperationResult<ProcessPriorityValue>.Success( new ProcessPriorityValue( value, false ) );
		}
		public ProcessOperationResult SetPriority( ProcessPriorityTarget target, int niceValue ) {
			this.SetTargets.Add( target );
			this.SetValues.Add( niceValue );
			this._values[ ( target.Kind, target.Identifier ) ] = niceValue;
			return ProcessOperationResult.Success();
		}
		public ProcessOperationResult<ProcessPriorityValue> AdjustPriority( ProcessPriorityTarget target, int increment ) {
			var current = this.GetPriority( target );
			if ( !current.Succeeded ) return current;
			var value = Math.Clamp( current.Value!.NiceValue + increment, -20, 19 );
			this.SetPriority( target, value );
			return this.GetPriority( target );
		}
	}
}
