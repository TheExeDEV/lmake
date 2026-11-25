using System.Reflection;

namespace lmake;

internal static class project
{
	public const string name = "lmake";
	public const string config_file_name = "lmake.json";
	public const string luaurc_file_name = ".luaurc";
	public const string default_input = "src";
	public const string default_output = "out";
	public const string gpp_compatible_version = "2.28";
	public const string stdin_name = "stdin";
	public const int gpp_warning_level = 2;
	public const int exit_success = 0;
	public const int exit_failure = 1;

	public static readonly IReadOnlyList<string> default_extensions = [".luau", ".lua"];
	public static readonly IReadOnlyList<string> module_extensions = [".luau", ".lua"];
	public static readonly IReadOnlyList<string> init_file_names = ["init.luau", "init.lua"];

	public static string get_version()
	{
		AssemblyInformationalVersionAttribute? attribute = typeof(project).Assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
		string version = attribute?.InformationalVersion ?? "0.0.0";
		int plus = version.IndexOf('+');

		return plus < 0 ? version : version[..plus];
	}
}
