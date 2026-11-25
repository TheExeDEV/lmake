namespace lmake;

public readonly record struct nothing;

public readonly record struct result<T>(bool state, T value, string message)
{
	public static result<T> ok(T value)
	{
		return new result<T>(true, value, "");
	}

	public static result<T> fail(string message)
	{
		return new result<T>(false, default!, message);
	}
}

public enum enumitem_line_endings
{
	Auto,
	Lf,
	Crlf,
}

public enum enumitem_token
{
	Name,
	Number,
	String,
	LongString,
	InterpolatedString,
	Symbol,
}

public readonly record struct tokeninfo(enumitem_token kind, int start, int length, string value);

public readonly record struct preprocessinfo(
	bool enabled,
	IReadOnlyList<string> syntax,
	IReadOnlyList<string> defines,
	IReadOnlyList<string> include_dirs,
	IReadOnlyList<string> prelude,
	bool allow_exec);

public readonly record struct requireinfo(bool enabled);

public readonly partial record struct settingsinfo(
	string root,
	string input,
	string output,
	IReadOnlyList<string> extensions,
	IReadOnlyList<string> exclude,
	bool copy_other_files,
	enumitem_line_endings line_endings,
	preprocessinfo preprocess,
	requireinfo requires)
{
	public static partial settingsinfo make_default(string root);

	public static partial result<settingsinfo> from_file(string path);

	public partial string to_json();
}

public readonly record struct pathmapinfo(string source_root, string output_root);

public readonly record struct rewriteinfo(string text, int changed, IReadOnlyList<string> warnings);

public readonly record struct buildinfo(int processed, int copied, int unchanged, IReadOnlyList<string> warnings);

public sealed partial class gppoptions
{
	public static readonly IReadOnlyList<string> luau_syntax =
	[
		"-U", "", "", "(", ",", ")", "(", ")", "#", "",
		"-M", "--#", "\\n", " ", " ", "\\n", "(", ")",
		"+cQQQ", "--[[#", "]]",
	];

	public IReadOnlyList<string> defines { get; internal set; } = [];

	public IReadOnlyList<string> include_dirs { get; internal set; } = [];

	public IReadOnlyList<string> pre_includes { get; internal set; } = [];

	public bool allow_exec { get; internal set; }

	public bool auto_switch_on_c_headers { get; internal set; }

	public bool no_current_dir_first { get; internal set; }

	public bool current_dir_last { get; internal set; }

	public bool dos_line_endings { get; internal set; }

	public int warning_level { get; internal set; }

	public string? input_file { get; internal set; }

	public string? output_file { get; internal set; }

	public bool also_stdout { get; internal set; }

	public bool show_help { get; internal set; }

	public bool show_version { get; internal set; }

	public static partial result<gppoptions> from_args(IReadOnlyList<string> args);

	public static partial string get_usage();
}

public sealed partial class gppprocessor
{
	public Action<string>? on_warning { get; set; }

	public Action<string>? on_include { get; set; }

	public static partial result<gppprocessor> make(gppoptions options);

	public partial result<string> process_file(string path);

	public partial result<string> process_text(string text, string file_name, string directory);

	public partial bool is_defined(string name);
}

public static partial class luaulexer
{
	public static partial IReadOnlyList<tokeninfo> read_tokens(string text);

	public static partial result<string> read_string_value(string text, tokeninfo token);
}

public sealed partial class luaurcresolver
{
	public static partial luaurcresolver make();

	public partial result<string> get_alias_path(string requirer_directory, string alias);
}

public sealed partial class requirerewriter
{
	public static partial requirerewriter make(luaurcresolver resolver);

	public partial result<rewriteinfo> apply(string text, string source_path, pathmapinfo map);
}

public sealed partial class builder
{
	public Action<string>? on_file { get; set; }

	public static partial builder make(settingsinfo settings);

	public partial result<buildinfo> run();

	public partial result<rewriteinfo> run_file(string source_path, string output_path);
}
