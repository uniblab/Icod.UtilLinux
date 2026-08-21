namespace Icod.UtilLinux.Renice;

/// <summary>Hosts the util-linux <c>renice</c> command.</summary>
internal static class Program {
	/// <summary>Runs <c>renice</c>.</summary>
	public static Task<int> Main( string[] args ) => Command.RunAsync( args );
}
