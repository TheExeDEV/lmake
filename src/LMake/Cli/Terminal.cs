namespace lmake;

internal static class terminal
{
	public static void write_error(string message)
	{
		Console.Error.WriteLine($"{project.name}: error: {message}");
	}

	public static void write_warning(string message)
	{
		Console.Error.WriteLine(message);
	}

	public static void write_line(string message)
	{
		Console.WriteLine(message);
	}

	public static int write_version()
	{
		Console.WriteLine($"{project.name} {project.get_version()}");

		return project.exit_success;
	}

	public static int write_help()
	{
		Console.WriteLine($$"""
			{{project.name}} {{project.get_version()}} - build tool for Luau with a built-in GPP preprocessor

			Usage:
			  {{project.name}} build [path] [options]   preprocess and convert requires for a project or a single file
			  {{project.name}} init [directory]         create {{project.config_file_name}} with default settings
			  {{project.name}} gpp [gpp options] [file] run the built-in preprocessor with a gpp-compatible command line
			  {{project.name}} version                  print the version
			  {{project.name}} help                     print this help

			Build options:
			  -c, --config <file>    settings file (default: <path>/{{project.config_file_name}})
			  -o, --output <path>    output directory, or output file for a single source file
			  -D<name>[=<value>]     define a preprocessor macro
			  -I<directory>          add an include directory for --#include
			  --no-preprocess        skip the preprocessor
			  --no-requires          keep .luaurc alias requires as they are
			  -q, --quiet            print errors and warnings only
			""");

		return project.exit_success;
	}
}
