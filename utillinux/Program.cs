namespace Icod.UtilLinux.Router;

/// <summary>Hosts the <c>utillinux</c> command router.</summary>
public static class Program {
	/// <summary>Runs the router.</summary>
	public static Task<int> Main(
		string[] args
	) => Command.RunAsync(
		args,
		Console.Out,
		Console.Error
	);
}
