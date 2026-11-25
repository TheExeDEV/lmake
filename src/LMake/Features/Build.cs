using System.Text;

namespace lmake;

file static partial class priv
{
	public static readonly UTF8Encoding utf8 = new UTF8Encoding(false);

	public static readonly Dictionary<enumitem_line_endings, Func<string, bool, string>> relevant_line_endings = new()
	{
		[enumitem_line_endings.Auto] = (text, source_has_crlf) => source_has_crlf ? text.Replace("\n", "\r\n") : text,
		[enumitem_line_endings.Lf] = (text, source_has_crlf) => text,
		[enumitem_line_endings.Crlf] = (text, source_has_crlf) => text.Replace("\n", "\r\n"),
	};
}

public sealed partial class builder
{
	private readonly settingsinfo settings;
	private readonly luaurcresolver resolver = luaurcresolver.make();
	private readonly string input_root;
	private readonly string output_root;

	private builder(settingsinfo settings)
	{
		this.settings = settings;
		input_root = Path.GetFullPath(Path.Combine(settings.root, settings.input));
		output_root = Path.GetFullPath(Path.Combine(settings.root, settings.output));
	}

	public static partial builder make(settingsinfo settings)
	{
		return new builder(settings);
	}

	public partial result<buildinfo> run()
	{
		if (!Directory.Exists(input_root))
		{
			return result<buildinfo>.fail($"input directory not found: {input_root}");
		}

		result<gppoptions> options = make_gpp_options();

		if (!options.state)
		{
			return result<buildinfo>.fail(options.message);
		}

		List<string> warnings = [];
		List<string> errors = [];
		int processed = 0;
		int copied = 0;
		int unchanged = 0;

		foreach (string source in get_source_files())
		{
			string relative = Path.GetRelativePath(input_root, source);
			string output = Path.Combine(output_root, relative);
			result<bool> written = is_module(source)
				? write_module(source, output, options.value, warnings)
				: write_copy(source, output);

			if (!written.state)
			{
				errors.Add(written.message);
				continue;
			}

			on_file?.Invoke(to_forward(relative));

			if (!written.value)
			{
				unchanged++;
			}
			else if (is_module(source))
			{
				processed++;
			}
			else
			{
				copied++;
			}
		}

		if (errors.Count > 0)
		{
			return result<buildinfo>.fail(string.Join(Environment.NewLine, errors.Concat(warnings)));
		}

		return result<buildinfo>.ok(new buildinfo(processed, copied, unchanged, warnings));
	}

	public partial result<rewriteinfo> run_file(string source_path, string output_path)
	{
		result<gppoptions> options = make_gpp_options();

		if (!options.state)
		{
			return result<rewriteinfo>.fail(options.message);
		}

		string source = Path.GetFullPath(source_path);
		string output = Path.GetFullPath(output_path);
		pathmapinfo map = new pathmapinfo(Path.GetDirectoryName(source)!, Path.GetDirectoryName(output)!);

		return make_module(source, map, options.value);
	}

	private result<gppoptions> make_gpp_options()
	{
		List<string> args = [.. settings.preprocess.syntax];

		foreach (string define in settings.preprocess.defines)
		{
			args.Add("-D" + define);
		}

		foreach (string directory in settings.preprocess.include_dirs)
		{
			args.Add("-I" + Path.GetFullPath(Path.Combine(settings.root, directory)));
		}

		foreach (string prelude in settings.preprocess.prelude)
		{
			args.Add("--include");
			args.Add(Path.GetFullPath(Path.Combine(settings.root, prelude)));
		}

		if (settings.preprocess.allow_exec)
		{
			args.Add("-x");
		}

		result<gppoptions> options = gppoptions.from_args(args);

		return options.state ? options : result<gppoptions>.fail($"preprocess syntax: {options.message}");
	}

	private IEnumerable<string> get_source_files()
	{
		bool output_inside_input = !Path.GetRelativePath(input_root, output_root).StartsWith("..", StringComparison.Ordinal)
			&& input_root != output_root;

		foreach (string file in Directory.EnumerateFiles(input_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
		{
			string relative = to_forward(Path.GetRelativePath(input_root, file));

			bool is_output = Path.GetFullPath(file).StartsWith(output_root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

			if (output_inside_input && is_output)
			{
				continue;
			}

			if (settings.exclude.Any(pattern => glob.is_path_match(pattern, relative)))
			{
				continue;
			}

			if (is_module(file) || settings.copy_other_files)
			{
				yield return file;
			}
		}
	}

	private bool is_module(string path)
	{
		return settings.extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
	}

	private result<rewriteinfo> make_module(string source, pathmapinfo map, gppoptions options)
	{
		string raw;

		try
		{
			raw = File.ReadAllText(source);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return result<rewriteinfo>.fail($"{source}: {exception.Message}");
		}

		string text = raw.Replace("\r", "");
		List<string> warnings = [];

		if (settings.preprocess.enabled)
		{
			result<gppprocessor> processor = gppprocessor.make(options);

			if (!processor.state)
			{
				return result<rewriteinfo>.fail(processor.message);
			}

			processor.value.on_warning = warnings.Add;
			string display_name = to_forward(Path.GetRelativePath(settings.root, source));
			result<string> preprocessed = processor.value.process_text(text, display_name, Path.GetDirectoryName(source)!);

			if (!preprocessed.state)
			{
				return result<rewriteinfo>.fail(preprocessed.message);
			}

			text = preprocessed.value;
		}

		int changed = 0;

		if (settings.requires.enabled)
		{
			result<rewriteinfo> rewritten = requirerewriter.make(resolver).apply(text, source, map);

			if (!rewritten.state)
			{
				return rewritten;
			}

			text = rewritten.value.text;
			changed = rewritten.value.changed;
			warnings.AddRange(rewritten.value.warnings);
		}

		text = priv.relevant_line_endings[settings.line_endings](text, raw.Contains("\r\n", StringComparison.Ordinal));

		return result<rewriteinfo>.ok(new rewriteinfo(text, changed, warnings));
	}

	private result<bool> write_module(string source, string output, gppoptions options, List<string> warnings)
	{
		result<rewriteinfo> module = make_module(source, new pathmapinfo(input_root, output_root), options);

		if (!module.state)
		{
			return result<bool>.fail(module.message);
		}

		warnings.AddRange(module.value.warnings);

		return write_if_changed(output, priv.utf8.GetBytes(module.value.text));
	}

	private static result<bool> write_copy(string source, string output)
	{
		try
		{
			return write_if_changed(output, File.ReadAllBytes(source));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return result<bool>.fail($"{source}: {exception.Message}");
		}
	}

	private static result<bool> write_if_changed(string output, byte[] content)
	{
		try
		{
			if (File.Exists(output) && File.ReadAllBytes(output).AsSpan().SequenceEqual(content))
			{
				return result<bool>.ok(false);
			}

			Directory.CreateDirectory(Path.GetDirectoryName(output)!);
			File.WriteAllBytes(output, content);

			return result<bool>.ok(true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return result<bool>.fail($"{output}: {exception.Message}");
		}
	}

	private static string to_forward(string path)
	{
		return path.Replace('\\', '/');
	}
}
