using System.Text;

namespace lmake;

internal static class initcommand
{
	public static int invoke(IReadOnlyList<string> args)
	{
		if (args.Count > 1)
		{
			terminal.write_error("init takes at most one directory");

			return project.exit_failure;
		}

		string directory = Path.GetFullPath(args.Count == 1 ? args[0] : Directory.GetCurrentDirectory());
		string path = Path.Combine(directory, project.config_file_name);

		if (File.Exists(path))
		{
			terminal.write_error($"{path} already exists");

			return project.exit_failure;
		}

		try
		{
			Directory.CreateDirectory(directory);
			File.WriteAllText(path, settingsinfo.make_default(directory).to_json(), new UTF8Encoding(false));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			terminal.write_error(exception.Message);

			return project.exit_failure;
		}

		terminal.write_line($"created {path}");

		return project.exit_success;
	}
}
