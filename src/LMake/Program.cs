using System.Text;

namespace lmake;

file static partial class priv
{
	public static readonly Dictionary<string, Func<IReadOnlyList<string>, int>> relevant_commands = new(StringComparer.Ordinal)
	{
		["build"] = buildcommand.invoke,
		["gpp"] = gppcommand.invoke,
		["init"] = initcommand.invoke,
		["version"] = args => terminal.write_version(),
		["--version"] = args => terminal.write_version(),
		["help"] = args => terminal.write_help(),
		["--help"] = args => terminal.write_help(),
		["-h"] = args => terminal.write_help(),
	};
}

internal static class program
{
	public static int Main(string[] args)
	{
		Console.OutputEncoding = new UTF8Encoding(false);

		if (args.Length == 0)
		{
			return terminal.write_help();
		}

		if (!priv.relevant_commands.TryGetValue(args[0], out Func<IReadOnlyList<string>, int>? command))
		{
			terminal.write_error($"unknown command '{args[0]}'");
			terminal.write_help();

			return project.exit_failure;
		}

		return command(args[1..]);
	}
}
