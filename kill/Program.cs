namespace Icod.UtilLinux.Kill;

/// <summary>Hosts the util-linux-compatible <c>kill</c> command.</summary>
public static class Program {
	/// <summary>Runs the command.</summary>
	public static Task<int> Main(
		string[] args
	) => Command.RunAsync(
		args,
		Console.Out,
		Console.Error
	);
}
