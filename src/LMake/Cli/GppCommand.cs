using System.Text;

namespace lmake;

internal static class gppcommand
{
	public static int invoke(IReadOnlyList<string> args)
	{
		result<gppoptions> parsed = gppoptions.from_args(args);

		if (!parsed.state)
		{
			Console.Error.WriteLine($"gpp: {parsed.message}");
			Console.Error.WriteLine(gppoptions.get_usage());

			return project.exit_failure;
		}

		gppoptions options = parsed.value;

		if (options.show_help)
		{
			Console.WriteLine(gppoptions.get_usage());

			return project.exit_success;
		}

		if (options.show_version)
		{
			Console.WriteLine($"{project.name} gpp {project.get_version()} (compatible with GPP {project.gpp_compatible_version})");

			return project.exit_success;
		}

		result<gppprocessor> processor = gppprocessor.make(options);

		if (!processor.state)
		{
			Console.Error.WriteLine(processor.message);

			return project.exit_failure;
		}

		processor.value.on_warning = terminal.write_warning;
		bool from_stdin = options.input_file is null or "-";
		result<string> output = from_stdin
			? processor.value.process_text(Console.In.ReadToEnd(), project.stdin_name, Directory.GetCurrentDirectory())
			: processor.value.process_file(options.input_file!);

		if (!output.state)
		{
			Console.Error.WriteLine(output.message);

			return project.exit_failure;
		}

		return write_output(options, output.value);
	}

	private static int write_output(gppoptions options, string text)
	{
		if (options.output_file is null)
		{
			Console.Write(text);

			return project.exit_success;
		}

		try
		{
			File.WriteAllText(options.output_file, text, new UTF8Encoding(false));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			Console.Error.WriteLine($"gpp: {exception.Message}");

			return project.exit_failure;
		}

		if (options.also_stdout)
		{
			Console.Write(text);
		}

		return project.exit_success;
	}
}
