using System.Text;

namespace lmake;

file static partial class priv
{
	public const string require_name = "require";
	public const string self_alias = "@self";
	public const string current_prefix = "./";
	public const string parent_prefix = "../";
	public const char default_quote = '"';

	public static readonly HashSet<string> relevant_member_access = [".", ":"];

	public static readonly HashSet<enumitem_token> relevant_string_tokens = [enumitem_token.String, enumitem_token.LongString];
}

internal readonly record struct requirecall(tokeninfo path_token, string value);

public sealed partial class requirerewriter
{
	private readonly luaurcresolver resolver;

	private requirerewriter(luaurcresolver resolver)
	{
		this.resolver = resolver;
	}

	public static partial requirerewriter make(luaurcresolver resolver)
	{
		return new requirerewriter(resolver);
	}

	public partial result<rewriteinfo> apply(string text, string source_path, pathmapinfo map)
	{
		IReadOnlyList<tokeninfo> tokens = luaulexer.read_tokens(text);
		List<string> warnings = [];
		StringBuilder output = new StringBuilder(text.Length);
		string full_source = Path.GetFullPath(source_path);
		string shown = to_shown(full_source);
		int copied = 0;
		int changed = 0;

		foreach (requirecall call in get_require_calls(text, tokens))
		{
			result<string> converted = get_converted(call.value, full_source, shown, map, warnings);

			if (!converted.state)
			{
				warnings.Add($"{shown}:{get_line(text, call.path_token.start)}: {converted.message}");
				continue;
			}

			char quote = call.path_token.kind == enumitem_token.String ? call.path_token.value[0] : priv.default_quote;
			output.Append(text, copied, call.path_token.start - copied);
			output.Append(to_literal(converted.value, quote));
			copied = call.path_token.start + call.path_token.length;
			changed++;
		}

		output.Append(text, copied, text.Length - copied);

		return result<rewriteinfo>.ok(new rewriteinfo(output.ToString(), changed, warnings));
	}

	private static IEnumerable<requirecall> get_require_calls(string text, IReadOnlyList<tokeninfo> tokens)
	{
		for (int i = 0; i < tokens.Count; i++)
		{
			tokeninfo token = tokens[i];

			if (token.kind != enumitem_token.Name || token.value != priv.require_name)
			{
				continue;
			}

			if (i > 0 && tokens[i - 1].kind == enumitem_token.Symbol && priv.relevant_member_access.Contains(tokens[i - 1].value))
			{
				continue;
			}

			int path_index = get_path_index(tokens, i + 1);

			if (path_index < 0)
			{
				continue;
			}

			result<string> value = luaulexer.read_string_value(text, tokens[path_index]);

			if (value.state && value.value.StartsWith('@') && !is_self_path(value.value))
			{
				yield return new requirecall(tokens[path_index], value.value);
			}
		}
	}

	private static int get_path_index(IReadOnlyList<tokeninfo> tokens, int next)
	{
		if (next < tokens.Count && priv.relevant_string_tokens.Contains(tokens[next].kind))
		{
			return next;
		}

		bool is_call = next + 2 < tokens.Count
			&& tokens[next].value == "("
			&& priv.relevant_string_tokens.Contains(tokens[next + 1].kind)
			&& tokens[next + 2].value == ")";

		return is_call ? next + 1 : -1;
	}

	private static bool is_self_path(string value)
	{
		return value == priv.self_alias || value.StartsWith(priv.self_alias + "/", StringComparison.Ordinal);
	}

	private result<string> get_converted(string value, string source_path, string shown, pathmapinfo map, List<string> warnings)
	{
		int slash = value.IndexOf('/');
		string alias = slash < 0 ? value[1..] : value[1..slash];
		string rest = slash < 0 ? "" : value[(slash + 1)..];
		result<string> alias_path = resolver.get_alias_path(Path.GetDirectoryName(source_path)!, alias);

		if (!alias_path.state)
		{
			return alias_path;
		}

		string target = rest.Length == 0 ? alias_path.value : Path.GetFullPath(Path.Combine(alias_path.value, rest));
		target = get_module_path(target);
		IReadOnlyList<string> candidates = get_module_files(target);

		if (candidates.Count == 0)
		{
			warnings.Add($"{shown}: '{value}' does not resolve to a module ({to_shown(target)})");
		}

		if (candidates.Count > 1)
		{
			warnings.Add($"{shown}: '{value}' is ambiguous: {string.Join(", ", candidates.Select(to_shown))}");
		}

		return make_relative(source_path, target, map);
	}

	private static string get_module_path(string target)
	{
		string extension = Path.GetExtension(target);
		bool has_module_extension = project.module_extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

		if (has_module_extension && File.Exists(target))
		{
			string stem = target[..^extension.Length];

			return project.init_file_names.Contains(Path.GetFileName(target)) ? Path.GetDirectoryName(target)! : stem;
		}

		return target;
	}

	private static IReadOnlyList<string> get_module_files(string target)
	{
		List<string> files = [];

		foreach (string extension in project.module_extensions)
		{
			if (File.Exists(target + extension))
			{
				files.Add(target + extension);
			}
		}

		foreach (string init in project.init_file_names)
		{
			string path = Path.Combine(target, init);

			if (File.Exists(path))
			{
				files.Add(path);
			}
		}

		return files;
	}

	private static bool is_init_module(string path)
	{
		return project.init_file_names.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
	}

	private static string to_mapped(string path, pathmapinfo map)
	{
		string relative = Path.GetRelativePath(map.source_root, path);
		bool is_inside = relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relative);

		return is_inside ? Path.GetFullPath(Path.Combine(map.output_root, relative)) : path;
	}

	private static bool is_inside(string parent, string child)
	{
		string relative = Path.GetRelativePath(parent, child);

		bool is_outside = relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative);

		return relative != "." && !is_outside;
	}

	private static string to_shown(string path)
	{
		string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);

		return to_forward(relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative);
	}

	private static string to_forward(string path)
	{
		return path.Replace('\\', '/');
	}

	private static result<string> make_relative(string source_path, string target, pathmapinfo map)
	{
		string mapped_source = to_mapped(source_path, map);
		string mapped_target = to_mapped(target, map);
		string source_directory = Path.GetDirectoryName(mapped_source)!;
		bool is_init = is_init_module(source_path);

		if (is_init && is_inside(source_directory, mapped_target))
		{
			return result<string>.ok(priv.self_alias + "/" + to_forward(Path.GetRelativePath(source_directory, mapped_target)));
		}

		string base_directory = is_init ? Path.GetDirectoryName(source_directory)! : source_directory;
		string direct = Path.GetRelativePath(base_directory, mapped_target);
		bool is_ancestor = direct.Split(Path.DirectorySeparatorChar).All(part => part is "." or "..");
		string parent_relative = Path.GetRelativePath(base_directory, Path.GetDirectoryName(mapped_target) ?? mapped_target);
		string relative = to_forward(is_ancestor ? Path.Combine(parent_relative, Path.GetFileName(mapped_target)) : direct);

		if (Path.IsPathRooted(relative))
		{
			return result<string>.fail($"cannot express a relative path from {source_path} to {target}");
		}

		if (relative.StartsWith("./", StringComparison.Ordinal))
		{
			return result<string>.ok(relative);
		}

		bool is_upward = relative.StartsWith(priv.parent_prefix, StringComparison.Ordinal);

		return result<string>.ok(is_upward ? relative : priv.current_prefix + relative);
	}

	private static string to_literal(string value, char quote)
	{
		StringBuilder literal = new StringBuilder(value.Length + 2);
		literal.Append(quote);

		foreach (char c in value)
		{
			if (c == '\\' || c == quote)
			{
				literal.Append('\\');
			}

			literal.Append(c);
		}

		return literal.Append(quote).ToString();
	}

	private static int get_line(string text, int index)
	{
		int line = 1;

		for (int i = 0; i < index && i < text.Length; i++)
		{
			if (text[i] == '\n')
			{
				line++;
			}
		}

		return line;
	}
}
