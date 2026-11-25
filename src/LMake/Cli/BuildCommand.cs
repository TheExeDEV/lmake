using System.Diagnostics;
using System.Text;

namespace lmake;

internal readonly record struct buildrequestinfo(
	string? path,
	string? config,
	string? output,
	IReadOnlyList<string> defines,
	IReadOnlyList<string> include_dirs,
	bool no_preprocess,
	bool no_requires,
	bool quiet);

file static partial class priv
{
	public delegate result<buildrequestinfo> optionreader(buildrequestinfo request, string? value);

	public readonly record struct optioninfo(bool takes_value, optionreader read);

	public static readonly Dictionary<string, optioninfo> relevant_options = new(StringComparer.Ordinal)
	{
		["-c"] = new optioninfo(true, (r, v) => result<buildrequestinfo>.ok(r with { config = v })),
		["--config"] = new optioninfo(true, (r, v) => result<buildrequestinfo>.ok(r with { config = v })),
		["-o"] = new optioninfo(true, (r, v) => result<buildrequestinfo>.ok(r with { output = v })),
		["--output"] = new optioninfo(true, (r, v) => result<buildrequestinfo>.ok(r with { output = v })),
		["-D"] = new optioninfo(true, (r, v) => result<buildrequestinfo>.ok(r with { defines = [.. r.defines, v!] })),
		["-I"] = new optioninfo(true, (r, v) => result<buildrequestinfo>.ok(r with { include_dirs = [.. r.include_dirs, v!] })),
		["--no-preprocess"] = new optioninfo(false, (r, v) => result<buildrequestinfo>.ok(r with { no_preprocess = true })),
		["--no-requires"] = new optioninfo(false, (r, v) => result<buildrequestinfo>.ok(r with { no_requires = true })),
		["-q"] = new optioninfo(false, (r, v) => result<buildrequestinfo>.ok(r with { quiet = true })),
		["--quiet"] = new optioninfo(false, (r, v) => result<buildrequestinfo>.ok(r with { quiet = true })),
	};

	public static readonly HashSet<string> relevant_attachable = ["-D", "-I"];
}

internal static class buildcommand
{
	public static int invoke(IReadOnlyList<string> args)
	{
		result<buildrequestinfo> request = read_request(args);

		if (!request.state)
		{
			terminal.write_error(request.message);

			return project.exit_failure;
		}

		string path = Path.GetFullPath(request.value.path ?? Directory.GetCurrentDirectory());

		if (File.Exists(path))
		{
			return invoke_file(request.value, path);
		}

		if (Directory.Exists(path))
		{
			return invoke_project(request.value, path);
		}

		terminal.write_error($"{path} does not exist");

		return project.exit_failure;
	}

	private static result<buildrequestinfo> read_request(IReadOnlyList<string> args)
	{
		buildrequestinfo request = new buildrequestinfo(null, null, null, [], [], false, false, false);

		for (int i = 0; i < args.Count; i++)
		{
			string arg = args[i];
			bool is_attached = arg.Length > 2 && priv.relevant_attachable.Contains(arg[..2]);
			string key = is_attached ? arg[..2] : arg;

			if (!priv.relevant_options.TryGetValue(key, out priv.optioninfo option))
			{
				if (arg.StartsWith('-') || request.path is not null)
				{
					return result<buildrequestinfo>.fail($"unexpected argument '{arg}'");
				}

				request = request with { path = arg };
				continue;
			}

			string? value = is_attached ? arg[2..] : null;

			if (option.takes_value && value is null)
			{
				if (i + 1 >= args.Count)
				{
					return result<buildrequestinfo>.fail($"'{arg}' needs a value");
				}

				i++;
				value = args[i];
			}

			result<buildrequestinfo> applied = option.read(request, value);

			if (!applied.state)
			{
				return applied;
			}

			request = applied.value;
		}

		return result<buildrequestinfo>.ok(request);
	}

	private static result<settingsinfo> read_settings(buildrequestinfo request, string root)
	{
		string config = Path.GetFullPath(request.config ?? Path.Combine(root, project.config_file_name));
		bool has_config = File.Exists(config);

		if (!has_config && request.config is not null)
		{
			return result<settingsinfo>.fail($"{config} does not exist");
		}

		result<settingsinfo> loaded = has_config
			? settingsinfo.from_file(config)
			: result<settingsinfo>.ok(settingsinfo.make_default(root));

		if (!loaded.state)
		{
			return loaded;
		}

		settingsinfo settings = loaded.value;
		preprocessinfo preprocess = settings.preprocess with
		{
			enabled = settings.preprocess.enabled && !request.no_preprocess,
			defines = [.. settings.preprocess.defines, .. request.defines],
			include_dirs = [.. settings.preprocess.include_dirs, .. request.include_dirs.Select(Path.GetFullPath)],
		};

		return result<settingsinfo>.ok(settings with
		{
			preprocess = preprocess,
			requires = new requireinfo(settings.requires.enabled && !request.no_requires),
		});
	}

	private static int invoke_project(buildrequestinfo request, string root)
	{
		result<settingsinfo> settings = read_settings(request, root);

		if (!settings.state)
		{
			terminal.write_error(settings.message);

			return project.exit_failure;
		}

		settingsinfo applied = request.output is null
			? settings.value
			: settings.value with { output = Path.GetFullPath(request.output) };
		builder build = builder.make(applied);
		Stopwatch timer = Stopwatch.StartNew();
		result<buildinfo> built = build.run();

		if (!built.state)
		{
			terminal.write_error(built.message);

			return project.exit_failure;
		}

		foreach (string warning in built.value.warnings)
		{
			terminal.write_warning(warning);
		}

		if (!request.quiet)
		{
			buildinfo info = built.value;
			string summary = $"built {info.processed} modules, copied {info.copied} files, {info.unchanged} unchanged";
			terminal.write_line($"{summary} ({timer.ElapsedMilliseconds} ms)");
		}

		return project.exit_success;
	}

	private static int invoke_file(buildrequestinfo request, string source)
	{
		string root = Directory.GetCurrentDirectory();
		result<settingsinfo> settings = read_settings(request, root);

		if (!settings.state)
		{
			terminal.write_error(settings.message);

			return project.exit_failure;
		}

		string output = Path.GetFullPath(request.output ?? source);
		result<rewriteinfo> built = builder.make(settings.value).run_file(source, output);

		if (!built.state)
		{
			terminal.write_error(built.message);

			return project.exit_failure;
		}

		foreach (string warning in built.value.warnings)
		{
			terminal.write_warning(warning);
		}

		if (request.output is null)
		{
			Console.Write(built.value.text);

			return project.exit_success;
		}

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(output)!);
			File.WriteAllText(output, built.value.text, new UTF8Encoding(false));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			terminal.write_error(exception.Message);

			return project.exit_failure;
		}

		return project.exit_success;
	}
}
