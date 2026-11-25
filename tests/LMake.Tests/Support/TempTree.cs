using System.Text;

namespace lmake.tests;

public sealed class temptree : IDisposable
{
	public string root { get; }

	public temptree()
	{
		root = Path.Combine(Path.GetTempPath(), "lmake-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
	}

	public string get_path(string relative)
	{
		return Path.GetFullPath(Path.Combine(root, relative));
	}

	public string add_file(string relative, string content)
	{
		string path = get_path(relative);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content, new UTF8Encoding(false));

		return path;
	}

	public string read_file(string relative)
	{
		return File.ReadAllText(get_path(relative));
	}

	public void add_copy(string source_directory)
	{
		foreach (string file in Directory.EnumerateFiles(source_directory, "*", SearchOption.AllDirectories))
		{
			string target = get_path(Path.GetRelativePath(source_directory, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target);
		}
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(root, true);
		}
		catch (IOException)
		{
		}
	}
}

public static class fixtures
{
	public static string get_root()
	{
		for (string? directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
		{
			string candidate = Path.Combine(directory, "tests", "fixtures");

			if (Directory.Exists(candidate))
			{
				return candidate;
			}
		}

		throw new DirectoryNotFoundException("tests/fixtures was not found above the test binaries");
	}

	public static string get_path(params string[] parts)
	{
		return Path.Combine([get_root(), .. parts]);
	}

	public static gppprocessor make_luau_processor(params string[] extra_args)
	{
		result<gppoptions> options = gppoptions.from_args([.. gppoptions.luau_syntax, .. extra_args]);
		Assert.True(options.state, options.message);
		result<gppprocessor> processor = gppprocessor.make(options.value);
		Assert.True(processor.state, processor.message);

		return processor.value;
	}

	public static result<string> get_luau_output(string text, params string[] extra_args)
	{
		return make_luau_processor(extra_args).process_text(text, "test.luau", Directory.GetCurrentDirectory());
	}
}
