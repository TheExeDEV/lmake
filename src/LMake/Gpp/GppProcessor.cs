using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace lmake;

file static partial class priv
{
	public const int stack_depth = 50;
	public const int max_args = 100;
	public const int max_mode_args = 10;
	public const string defined_name = "defined";
	public const string mode_quote = "\"";
	public const int verbose_warnings = 1;
	public const int all_warnings = 2;

	public static readonly char[] white_chars = [' ', '\t', '\n'];
}

internal sealed class gpperror(string message) : Exception(message);

internal enum enumitem_meta
{
	Define,
	Undef,
	Ifdef,
	Ifndef,
	Else,
	Endif,
	Include,
	Exec,
	Defeval,
	Ifeq,
	Ifneq,
	Eval,
	If,
	Mode,
	Line,
	File,
	Elif,
	Error,
	Warning,
	Date,
	Sinclude,
}

public sealed partial class gppprocessor
{
	private sealed class inputcontext
	{
		public string buf = "\n";
		public int offset;
		public bool eof;
		public string file_name = project.stdin_name;
		public string directory = Directory.GetCurrentDirectory();
		public bool is_file;
		public int line = 1;
		public int argc;
		public string[] argv = [];
		public string[]? named_args;
		public StringBuilder output = new StringBuilder();
		public bool in_comment;
		public enumitem_ambience ambience = enumitem_ambience.Text;
		public bool may_have_args;
	}

	private sealed class metacall
	{
		public enumitem_meta id;
		public int nparam;
		public int p1start;
		public int p1end;
		public int p2start;
		public int p2end;
		public bool named_args;
		public List<int> argb = [];
		public List<int> arge = [];
	}

	private readonly record struct metainfo(enumitem_meta id, int expected_params, Action<gppprocessor, metacall> run);

	private readonly record struct modecall(string? option, List<string> args, specs target);

	private static readonly Dictionary<string, metainfo> relevant_meta_macros = new()
	{
		["define"] = new metainfo(enumitem_meta.Define, 2, (g, c) => g.invoke_define(c, false)),
		["undef"] = new metainfo(enumitem_meta.Undef, 1, (g, c) => g.invoke_undef(c)),
		["ifdef"] = new metainfo(enumitem_meta.Ifdef, 1, (g, c) => g.invoke_ifdef(c, "#ifdef", false)),
		["ifndef"] = new metainfo(enumitem_meta.Ifndef, 1, (g, c) => g.invoke_ifdef(c, "#ifndef", true)),
		["else"] = new metainfo(enumitem_meta.Else, 0, (g, c) => g.invoke_else(c)),
		["endif"] = new metainfo(enumitem_meta.Endif, 0, (g, c) => g.invoke_endif(c)),
		["include"] = new metainfo(enumitem_meta.Include, 1, (g, c) => g.invoke_include_macro(c, "#include", false)),
		["exec"] = new metainfo(enumitem_meta.Exec, 1, (g, c) => g.invoke_exec(c)),
		["defeval"] = new metainfo(enumitem_meta.Defeval, 2, (g, c) => g.invoke_define(c, true)),
		["ifeq"] = new metainfo(enumitem_meta.Ifeq, 2, (g, c) => g.invoke_ifeq(c, "#ifeq", false)),
		["ifneq"] = new metainfo(enumitem_meta.Ifneq, 2, (g, c) => g.invoke_ifeq(c, "#ifneq", true)),
		["eval"] = new metainfo(enumitem_meta.Eval, 1, (g, c) => g.invoke_eval(c)),
		["if"] = new metainfo(enumitem_meta.If, 1, (g, c) => g.invoke_if(c)),
		["mode"] = new metainfo(enumitem_meta.Mode, 2, (g, c) => g.invoke_mode(c)),
		["line"] = new metainfo(enumitem_meta.Line, 0, (g, c) => g.add_output(arithmetic.to_text(g.ctx.line), false)),
		["file"] = new metainfo(enumitem_meta.File, 0, (g, c) => g.add_output(g.ctx.file_name, false)),
		["elif"] = new metainfo(enumitem_meta.Elif, 1, (g, c) => g.invoke_elif(c)),
		["error"] = new metainfo(enumitem_meta.Error, 1, (g, c) => g.invoke_error(c)),
		["warning"] = new metainfo(enumitem_meta.Warning, 1, (g, c) => g.invoke_warning(c)),
		["date"] = new metainfo(enumitem_meta.Date, 1, (g, c) => g.invoke_date(c)),
		["sinclude"] = new metainfo(enumitem_meta.Sinclude, 1, (g, c) => g.invoke_include_macro(c, "#sinclude", true)),
	};

	private static readonly Dictionary<string, Action<gppprocessor, modecall>> relevant_mode_commands = new()
	{
		["quote"] = (g, m) => g.apply_mode_quote(m),
		["comment"] = (g, m) => g.apply_mode_comment(m, "comment", "ccc"),
		["string"] = (g, m) => g.apply_mode_comment(m, "string", "sss"),
		["save"] = (g, m) => g.apply_mode_save(m),
		["push"] = (g, m) => g.apply_mode_save(m),
		["restore"] = (g, m) => g.apply_mode_restore(m),
		["pop"] = (g, m) => g.apply_mode_restore(m),
		["standard"] = (g, m) => g.apply_mode_standard(m),
		["user"] = (g, m) => g.apply_mode_user(m),
		["meta"] = (g, m) => g.apply_mode_meta(m),
		["preservelf"] = (g, m) => g.apply_mode_preservelf(m),
		["nocomment"] = (g, m) => g.apply_mode_nocomment(m),
		["nostring"] = (g, m) => g.apply_mode_nocomment(m),
		["charset"] = (g, m) => g.apply_mode_charset(m),
	};

	private static readonly Dictionary<string, Action<specs, charset>> relevant_charsets = new(StringComparer.OrdinalIgnoreCase)
	{
		["op"] = (s, c) => s.op_set = c,
		["par"] = (s, c) => s.ext_op_set = c,
		["id"] = (s, c) => s.id_set = c,
	};

	private static readonly Dictionary<string, bool> relevant_switches = new(StringComparer.OrdinalIgnoreCase)
	{
		["1"] = true,
		["on"] = true,
		["0"] = false,
		["off"] = false,
	};

	private readonly gppoptions options;
	private readonly Dictionary<string, macro> macros = new Dictionary<string, macro>(StringComparer.Ordinal);
	private readonly int[] commented = new int[priv.stack_depth];
	private int if_level;
	private int parse_level;
	private specs current;
	private inputcontext ctx = new inputcontext();

	private gppprocessor(gppoptions options)
	{
		this.options = options;
		current = options.get_initial_specs();
	}

	public static partial result<gppprocessor> make(gppoptions options)
	{
		gppprocessor processor = new gppprocessor(options);

		foreach (string define in options.defines)
		{
			result<nothing> added = processor.add_cmdline_define(define);

			if (!added.state)
			{
				return result<gppprocessor>.fail($"<command line>: error: {added.message}");
			}
		}

		foreach (macro m in processor.macros.Values)
		{
			m.define_specs ??= processor.current.clone();
			processor.rebuild_arg_refs(m);
		}

		return result<gppprocessor>.ok(processor);
	}

	public partial result<string> process_file(string path)
	{
		string text;

		try
		{
			text = read_source(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return result<string>.fail($"{path}: error: {exception.Message}");
		}

		string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();

		return invoke_top_level(text, path, directory);
	}

	public partial result<string> process_text(string text, string file_name, string directory)
	{
		return invoke_top_level(text.Replace("\r", ""), file_name, directory);
	}

	public partial bool is_defined(string name)
	{
		return macros.ContainsKey(name);
	}

	internal static string read_source(string path)
	{
		return File.ReadAllText(path).Replace("\r", "");
	}

	private result<string> invoke_top_level(string text, string file_name, string directory)
	{
		inputcontext top = new inputcontext
		{
			buf = "\n" + text,
			file_name = file_name,
			directory = directory,
			is_file = true,
			output = new StringBuilder(text.Length),
		};
		ctx = top;
		if_level = 0;
		commented[0] = 0;

		try
		{
			foreach (string pre_include in options.pre_includes)
			{
				invoke_include(pre_include, false, true);
			}

			ctx = top;
			invoke_context();
		}
		catch (gpperror error)
		{
			return result<string>.fail(error.Message);
		}

		string output = top.output.ToString();

		return result<string>.ok(options.dos_line_endings ? output.Replace("\n", "\r\n") : output);
	}

	private gpperror make_error(string message)
	{
		return new gpperror($"{ctx.file_name}:{ctx.line}: error: {message}");
	}

	private void add_warning(string message)
	{
		on_warning?.Invoke($"{ctx.file_name}:{ctx.line}: warning: {message}");
	}

	private void apply_result(result<nothing> outcome)
	{
		if (!outcome.state)
		{
			throw make_error(outcome.message);
		}
	}

	private char get_char(int pos)
	{
		int index = ctx.offset + pos;

		return (uint)index < (uint)ctx.buf.Length ? ctx.buf[index] : '\0';
	}

	private string get_text(int start, int length)
	{
		int index = ctx.offset + start;

		if (length <= 0 || index >= ctx.buf.Length)
		{
			return "";
		}

		if (index < 0)
		{
			length += index;
			index = 0;
		}

		return ctx.buf.Substring(index, Math.Min(length, ctx.buf.Length - index));
	}

	private void apply_shift(int length)
	{
		if (length <= 1)
		{
			return;
		}

		int new_offset = ctx.offset + length - 1;

		if (ctx.is_file)
		{
			int last = Math.Min(new_offset, ctx.buf.Length - 1);

			for (int k = ctx.offset + 1; k <= last; k++)
			{
				if (ctx.buf[k] == '\n')
				{
					ctx.line++;
				}
			}
		}

		ctx.offset = new_offset;
		ctx.eof = ctx.offset + 1 >= ctx.buf.Length;
	}

	private bool is_commented()
	{
		return commented[if_level] != 0;
	}

	private void add_output(string s, bool process_quotes)
	{
		if (is_commented())
		{
			return;
		}

		char quote = current.user.quote_char;

		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];

			if (process_quotes && c != '\0' && c == quote)
			{
				i++;

				if (i == s.Length)
				{
					return;
				}

				c = s[i];
			}

			if (c != '\0')
			{
				ctx.output.Append(c);
			}
		}
	}

	private void add_raw_output(string s)
	{
		ctx.output.Append(s);
	}

	private bool apply_trim(ref int start, ref int end)
	{
		while (start < end && syntax.is_white(get_char(start)))
		{
			start++;
		}

		while (start < end && syntax.is_white(get_char(end - 1)))
		{
			end--;
		}

		return start < end;
	}

	private bool is_identifier(int start, int end)
	{
		return start < end && get_identifier_end(start) == end;
	}

	private int get_identifier_end(int start)
	{
		char c = get_char(start);

		if (c == '\0')
		{
			return start;
		}

		char quote = current.user.quote_char;

		if (quote != '\0' && c == quote)
		{
			c = get_char(start + 1);

			if (c == '\0')
			{
				return start + 1;
			}

			if (syntax.is_delim(c))
			{
				return start + 2;
			}

			start += 2;
			c = get_char(start);
		}

		while (!syntax.is_delim(c))
		{
			start++;
			c = get_char(start);
		}

		return start;
	}

	private int get_identifiers_end(int start)
	{
		while (true)
		{
			int end = get_identifier_end(start);

			if (end == start)
			{
				return end;
			}

			start = end;
		}
	}

	private bool is_class_match(int code, char c)
	{
		return code switch
		{
			syntax.alpha => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z',
			syntax.alpha_or_white => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or ' ' or '\t' or '\n',
			syntax.digit => c is >= '0' and <= '9',
			syntax.ident => current.id_set.has(c),
			syntax.tab => c == '\t',
			syntax.newline => c == '\n',
			syntax.operator_char => current.op_set.has(c),
			syntax.operator_or_paren => current.ext_op_set.has(c) || current.op_set.has(c),
			_ => false,
		};
	}

	private int get_run_end(int pos, bool include_newline)
	{
		char c = get_char(pos);

		while (c is ' ' or '\t' || (include_newline && c == '\n'))
		{
			pos++;
			c = get_char(pos);
		}

		return pos;
	}

	private bool is_sequence_match(string s, ref int pos)
	{
		int i = pos;

		foreach (char sc in s)
		{
			if (!syntax.is_special(sc))
			{
				if (get_char(i) != sc)
				{
					return false;
				}

				i++;
				continue;
			}

			int code = syntax.get_code(sc);
			char first = get_char(i);
			bool match;

			if (code is syntax.one_or_more_blanks or syntax.one_or_more_white)
			{
				bool include_newline = code == syntax.one_or_more_white;
				match = first is ' ' or '\t' || (include_newline && first == '\n');
				i = match ? get_run_end(i, include_newline) : i + 1;
			}
			else if (code is syntax.zero_or_more_blanks or syntax.zero_or_more_white)
			{
				match = true;
				i = get_run_end(i, code == syntax.zero_or_more_white);
			}
			else
			{
				match = is_class_match(code, first);
				i++;
			}

			if (syntax.is_negated(sc))
			{
				match = !match;
			}

			if (!match)
			{
				return false;
			}
		}

		pos = i;

		return true;
	}

	private bool is_end_match(string s, ref int pos)
	{
		if (s.Length == 0)
		{
			return true;
		}

		if (get_char(pos) == '\0' && s == "\n")
		{
			return true;
		}

		if (!is_sequence_match(s, ref pos))
		{
			return false;
		}

		if (current.preserve_lf && syntax.is_white(get_char(pos - 1)))
		{
			pos--;
		}

		return true;
	}

	private bool is_start_match(string s, ref int pos)
	{
		if (s.Length > 0 && syntax.is_special(s[0]))
		{
			char previous = get_char(pos - 1);
			int code = syntax.get_code(s[0]);
			bool match = code switch
			{
				syntax.one_or_more_blanks => previous is ' ' or '\t',
				syntax.one_or_more_white => previous is ' ' or '\t' or '\n',
				syntax.zero_or_more_blanks or syntax.zero_or_more_white => true,
				_ => is_class_match(code, previous),
			};

			if (syntax.is_negated(s[0]))
			{
				match = !match;
			}

			if (!match)
			{
				return false;
			}

			s = s[1..];
		}

		return is_sequence_match(s, ref pos);
	}

	private int get_comment_end(string end, char quote, char warn, int pos, enumitem_comment_flags flags)
	{
		while (true)
		{
			char c = get_char(pos);
			int probe = pos;

			if (is_end_match(end, ref probe))
			{
				return pos;
			}

			if (c == '\0')
			{
				throw make_error("Input ended while scanning a comment/string");
			}

			if (warn != '\0' && c == warn)
			{
				warn = '\0';

				if (options.warning_level >= priv.all_warnings)
				{
					add_warning("possible comment/string termination problem");
				}
			}

			bool parses_macros = (flags & enumitem_comment_flags.ParseMacros) != 0;
			bool is_quote = quote != '\0' && c == quote;
			bool is_macro_quote = parses_macros && current.user.quote_char != '\0' && c == current.user.quote_char;
			pos += is_quote || is_macro_quote ? 2 : 1;
		}
	}

	private void apply_comment_skip(ref int pos, enumitem_ambience mode, bool silent_only)
	{
		if (ctx.in_comment)
		{
			return;
		}

		bool found = true;

		while (found)
		{
			found = false;

			if (get_char(pos) == '\0')
			{
				return;
			}

			foreach (commentspec spec in current.comments)
			{
				enumitem_comment_flags flags = spec.get_flags(mode);

				if ((flags & enumitem_comment_flags.Ignore) != 0 || (silent_only && flags != enumitem_comment_flags.Comment))
				{
					continue;
				}

				if (is_start_match(spec.start, ref pos))
				{
					pos = get_comment_end(spec.end, spec.quote, spec.warn, pos, flags);
					is_end_match(spec.end, ref pos);
					found = true;
					break;
				}
			}
		}
	}

	private bool is_comment_or_white(int start, int end, enumitem_ambience mode)
	{
		while (start < end)
		{
			apply_comment_skip(ref start, mode, true);

			if (start < end)
			{
				char c = get_char(start);
				start++;

				if (!syntax.is_white(c))
				{
					return false;
				}
			}
		}

		return true;
	}

	private string get_text_without_comments(int start, int end, enumitem_ambience mode)
	{
		StringBuilder text = new StringBuilder(Math.Max(0, end - start));
		char quote = current.user.quote_char;

		while (start < end)
		{
			apply_comment_skip(ref start, mode, true);

			if (start >= end)
			{
				continue;
			}

			char c = get_char(start);
			start++;
			text.Append(c);

			if (quote != '\0' && c == quote && start < end)
			{
				text.Append(get_char(start));
				start++;
			}
		}

		return text.ToString();
	}

	private void add_specs(specs source)
	{
		specs pushed = source.clone();
		pushed.next = current;
		current = pushed;
	}

	private void rem_specs()
	{
		current = current.next ?? throw make_error("#mode restore without #mode save");
	}

	private void rebuild_arg_refs(macro m)
	{
		if (m.arg_names is not null)
		{
			return;
		}

		m.named_arg_count = macro.no_arg_refs;
		string reference = current.user.arg_ref;
		char quote = current.user.quote_char;
		string text = m.text;

		for (int i = 0; i < text.Length; i++)
		{
			if (quote != '\0' && text[i] == quote)
			{
				i++;
				continue;
			}

			bool has_reference = string.CompareOrdinal(text, i, reference, 0, reference.Length) == 0;
			int digit_index = i + reference.Length;

			if (has_reference && digit_index < text.Length && text[digit_index] is >= '1' and <= '9')
			{
				m.named_arg_count = macro.named_args_from_refs;

				return;
			}
		}
	}

	private result<nothing> add_cmdline_define(string s)
	{
		int length = 0;

		while (length < s.Length && s[length] != '=' && s[length] != '(')
		{
			length++;
		}

		macro m = new macro { name = s[..length] };

		if (length < s.Length && s[length] == '(')
		{
			List<string> names = [];

			do
			{
				length++;
				int end = length;

				while (end < s.Length && !syntax.is_delim(s[end]))
				{
					end++;
				}

				if (end >= s.Length || (s[end] != ',' && s[end] != ')'))
				{
					return result<nothing>.fail("invalid syntax in -D declaration");
				}

				if (end > length)
				{
					names.Add(s[length..end]);
				}

				length = end;
			}
			while (s[length] != ')');

			length++;
			m.arg_names = [.. names];
			m.named_arg_count = names.Count;
		}

		if (length < s.Length && s[length] != '=')
		{
			return result<nothing>.fail("invalid syntax in -D declaration");
		}

		m.text = length < s.Length ? s[(length + 1)..] : "";
		macros[m.name] = m;

		return result<nothing>.ok(new nothing());
	}

	private bool read_user_call(ref int id_start, out int id_end, out int short_end, out int long_end,
		List<int> argb, List<int> arge, bool check_id, out macro? found, enumitem_ambience mode)
	{
		id_end = -1;
		short_end = -1;
		long_end = -1;
		found = null;
		argb.Clear();
		arge.Clear();

		if (!is_start_match(current.user.start, ref id_start))
		{
			return false;
		}

		id_end = get_identifier_end(id_start);

		if (id_end != 0 && get_char(id_end - 1) == '\0')
		{
			return false;
		}

		short_end = id_end;

		if (!is_end_match(current.user.end, ref short_end))
		{
			short_end = -1;
		}

		int pos = id_end;
		bool has_args = is_sequence_match(current.user.arg_start, ref pos);

		if (check_id)
		{
			found = macros.GetValueOrDefault(get_text(id_start, id_end - id_start));
			has_args = has_args && found is not null;
		}

		if (!has_args)
		{
			return short_end >= 0;
		}

		while (true)
		{
			if (argb.Count >= priv.max_args)
			{
				throw make_error("too many macro parameters");
			}

			argb.Add(pos);
			arge.Add(pos);
			int depth = 0;
			bool is_last = false;

			while (true)
			{
				pos = get_identifiers_end(pos);
				apply_comment_skip(ref pos, mode, false);
				char c = get_char(pos);

				if (c == '\0')
				{
					return short_end >= 0;
				}

				if (current.user.stack_chars.Contains(c))
				{
					depth++;
				}

				if (depth != 0)
				{
					if (current.user.unstack_chars.Contains(c))
					{
						depth--;
					}
				}
				else
				{
					arge[^1] = pos;

					if (is_sequence_match(current.user.arg_sep, ref pos))
					{
						break;
					}

					if (is_end_match(current.user.arg_end, ref pos))
					{
						is_last = true;
						break;
					}
				}

				pos++;
			}

			if (is_last)
			{
				long_end = pos;

				return true;
			}
		}
	}

	private int read_meta_args(int start, metacall call, out int macro_end)
	{
		int pos = start;
		macro_end = -1;

		if (!is_sequence_match(current.meta.arg_start, ref pos))
		{
			if (!is_end_match(current.meta.end, ref pos))
			{
				return -1;
			}

			macro_end = pos;

			return 0;
		}

		call.p1start = pos;

		if (call.named_args && read_user_call(ref pos, out int id_end, out int short_end, out int long_end,
			call.argb, call.arge, false, out _, enumitem_ambience.Meta))
		{
			call.p1start = pos;
			call.p1end = id_end;
			pos = long_end >= 0 ? long_end : short_end;
			call.named_args = long_end >= 0;

			if (!is_sequence_match(current.meta.arg_sep, ref pos))
			{
				if (!is_end_match(current.meta.arg_end, ref pos))
				{
					throw make_error("#define/#defeval requires an identifier or a single macro call");
				}

				macro_end = pos;

				return 1;
			}
		}
		else
		{
			call.named_args = false;
			int depth = 0;

			while (true)
			{
				pos = get_identifiers_end(pos);
				apply_comment_skip(ref pos, enumitem_ambience.Meta, false);
				char c = get_char(pos);

				if (c != '\0' && current.meta.stack_chars.Contains(c))
				{
					depth++;
				}

				if (depth != 0)
				{
					if (c != '\0' && current.meta.unstack_chars.Contains(c))
					{
						depth--;
					}
				}
				else
				{
					call.p1end = pos;

					if (is_sequence_match(current.meta.arg_sep, ref pos))
					{
						break;
					}

					if (is_end_match(current.meta.arg_end, ref pos))
					{
						macro_end = pos;

						return 1;
					}
				}

				if (get_char(pos) == '\0')
				{
					throw make_error("unfinished macro argument");
				}

				pos++;
			}
		}

		call.p2start = pos;
		int nesting = 0;

		while (true)
		{
			pos = get_identifiers_end(pos);
			apply_comment_skip(ref pos, enumitem_ambience.Meta, false);
			char c = get_char(pos);

			if (c != '\0' && current.meta.stack_chars.Contains(c))
			{
				nesting++;
			}

			if (nesting != 0)
			{
				if (c != '\0' && current.meta.unstack_chars.Contains(c))
				{
					nesting--;
				}
			}
			else
			{
				call.p2end = pos;

				if (is_end_match(current.meta.arg_end, ref pos))
				{
					break;
				}
			}

			if (get_char(pos) == '\0')
			{
				throw make_error("unfinished macro");
			}

			pos++;
		}

		macro_end = pos;

		return 2;
	}

	private string invoke_text(string text, enumitem_ambience ambience)
	{
		if (text.Length == 0)
		{
			return "";
		}

		inputcontext parent = ctx;
		ctx = new inputcontext
		{
			buf = "\n" + text,
			argc = parent.argc,
			argv = parent.argv,
			file_name = parent.file_name,
			directory = parent.directory,
			line = parent.line,
			named_args = parent.named_args,
			in_comment = parent.in_comment,
			ambience = ambience,
			may_have_args = parent.may_have_args,
		};

		try
		{
			invoke_context();

			return ctx.output.ToString();
		}
		finally
		{
			ctx = parent;
		}
	}

	private string invoke_range(int start, int end, enumitem_ambience ambience)
	{
		return invoke_text(get_text(start, end - start), ambience);
	}

	private void invoke_context()
	{
		while (!ctx.eof)
		{
			invoke_parse();
		}
	}

	private string invoke_expression(int start, int end)
	{
		bool added = !macros.ContainsKey(priv.defined_name);

		if (added)
		{
			macros[priv.defined_name] = new macro
			{
				name = priv.defined_name,
				named_arg_count = macro.defined_operator,
				define_specs = current.clone(),
			};
		}
		else
		{
			add_warning("the defined(...) macro is already defined");
		}

		string text = invoke_range(start, end, enumitem_ambience.Meta);

		if (added)
		{
			bool is_intact = macros.TryGetValue(priv.defined_name, out macro? defined) && defined.named_arg_count == macro.defined_operator;

			if (is_intact)
			{
				macros.Remove(priv.defined_name);
			}
			else
			{
				add_warning("the defined(...) macro was redefined in expression");
			}
		}

		try
		{
			result<int> value = arithmetic.evaluate(text);

			return value.state ? arithmetic.to_text(value.value) : text;
		}
		catch (gpperror error)
		{
			throw make_error(error.Message);
		}
	}

	private void invoke_parse()
	{
		parse_level++;

		if (parse_level == priv.stack_depth)
		{
			throw make_error("Stack depth exceeded during parse");
		}

		try
		{
			invoke_parse_step();
		}
		finally
		{
			parse_level--;
		}
	}

	private void invoke_parse_step()
	{
		if (!ctx.in_comment && invoke_comment())
		{
			return;
		}

		if (invoke_meta_macro() || invoke_user_macro())
		{
			return;
		}

		int pos = 1;

		if (is_sequence_match(current.user.arg_ref, ref pos) && ctx.may_have_args)
		{
			char c = get_char(pos);

			if (c is >= '1' and <= '9')
			{
				int index = c - '1';

				if (index < ctx.argc)
				{
					add_output(ctx.argv[index], false);
				}

				apply_shift(pos + 1);

				return;
			}
		}

		pos = get_identifier_end(1);

		if (pos == 1)
		{
			pos = 2;
		}

		add_output(get_text(1, pos - 1), true);
		apply_shift(pos);
	}

	private bool invoke_comment()
	{
		int start = 1;

		foreach (commentspec spec in current.comments)
		{
			enumitem_comment_flags flags = spec.get_flags(ctx.ambience);

			if ((flags & enumitem_comment_flags.Ignore) != 0 || !is_start_match(spec.start, ref start))
			{
				continue;
			}

			int content_end = get_comment_end(spec.end, spec.quote, spec.warn, start, flags);
			int end = content_end;
			is_end_match(spec.end, ref end);
			bool outputs_delims = (flags & enumitem_comment_flags.OutputDelim) != 0;
			bool outputs_text = (flags & enumitem_comment_flags.OutputText) != 0;

			if (outputs_delims)
			{
				add_output(get_text(1, start - 1), false);
			}

			if ((flags & enumitem_comment_flags.ParseMacros) != 0)
			{
				ctx.in_comment = true;
				string evaluated = invoke_range(start, content_end, ctx.ambience);

				if (outputs_text)
				{
					add_output(evaluated, false);
				}

				ctx.in_comment = false;
			}
			else if (outputs_text)
			{
				add_output(get_text(start, content_end - start), false);
			}

			if (outputs_delims)
			{
				add_output(get_text(content_end, end - content_end), false);
			}

			apply_shift(end);

			return true;
		}

		return false;
	}

	private bool invoke_user_macro()
	{
		int id_start = 1;
		List<int> argb = [];
		List<int> arge = [];

		if (!read_user_call(ref id_start, out int id_end, out int short_end, out int long_end, argb, arge, true, out macro? found,
			enumitem_ambience.User))
		{
			return false;
		}

		if (short_end >= 0 && ctx.named_args is not null)
		{
			int index = Array.IndexOf(ctx.named_args, get_text(id_start, id_end - id_start));

			if (index >= 0)
			{
				if (index < ctx.argc)
				{
					add_output(ctx.argv[index], false);
				}

				apply_shift(short_end);

				return true;
			}
		}

		if (found is null)
		{
			return false;
		}

		int macro_end = long_end >= 0 ? long_end : short_end;
		int argc = long_end >= 0 ? argb.Count : 0;

		if (found.named_arg_count == macro.defined_operator)
		{
			if (argc != 1)
			{
				return false;
			}

			string name = get_text_without_comments(argb[0], arge[0], enumitem_ambience.User).Trim(priv.white_chars);
			add_raw_output(macros.ContainsKey(name) ? "1" : "0");
			apply_shift(macro_end);

			return true;
		}

		if (found.text.Length == 0)
		{
			apply_shift(macro_end);

			return true;
		}

		string[] argv = new string[argc];

		for (int i = 0; i < argc; i++)
		{
			argv[i] = invoke_range(argb[i], arge[i], enumitem_ambience.User);
		}

		specs definition = found.define_specs!;
		bool is_alias = found.named_arg_count == macro.no_arg_refs && long_end >= 0 && definition.user.end.Length == 0;
		string body = is_alias ? make_alias_body(found, definition, argv) : found.text;
		inputcontext parent = ctx;
		ctx = new inputcontext
		{
			buf = "\n" + body,
			output = parent.output,
			argc = argc,
			argv = argv,
			file_name = parent.file_name,
			directory = parent.directory,
			line = parent.line,
			may_have_args = !is_alias,
			named_args = found.arg_names,
			in_comment = found.defined_in_comment,
			ambience = enumitem_ambience.Meta,
		};
		add_specs(definition);

		try
		{
			invoke_context();
		}
		finally
		{
			rem_specs();
			ctx = parent;
		}

		apply_shift(macro_end);

		return true;
	}

	private static string make_alias_body(macro found, specs definition, string[] argv)
	{
		StringBuilder body = new StringBuilder(found.text.TrimEnd(priv.white_chars));
		body.Append(syntax.to_literal(definition.user.arg_start));

		for (int i = 0; i < argv.Length; i++)
		{
			if (i > 0)
			{
				body.Append(syntax.to_literal(definition.user.arg_sep));
			}

			body.Append(argv[i]);
		}

		body.Append(syntax.to_literal(definition.user.arg_end));

		return body.ToString();
	}

	private bool invoke_meta_macro()
	{
		int name_start = 1;

		if (!is_start_match(current.meta.start, ref name_start))
		{
			return false;
		}

		int name_end = get_identifier_end(name_start);

		if (name_end != 0 && get_char(name_end - 1) == '\0')
		{
			return false;
		}

		if (!relevant_meta_macros.TryGetValue(get_text(name_start, name_end - name_start), out metainfo meta))
		{
			return false;
		}

		metacall call = new metacall { id = meta.id, named_args = meta.id is enumitem_meta.Define or enumitem_meta.Defeval };
		bool is_mode = meta.id == enumitem_meta.Mode;

		if (is_mode)
		{
			add_specs(current);
			current.preserve_lf = true;
			current.rem_comment(priv.mode_quote);
			current.add_comment("sss", priv.mode_quote, priv.mode_quote, '\\', '\n');
		}

		int macro_end;

		try
		{
			call.nparam = read_meta_args(name_end, call, out macro_end);

			if (call.nparam == -1)
			{
				return false;
			}

			if (call.nparam == 2 && syntax.is_white_separator(current.meta.arg_sep)
				&& is_comment_or_white(call.p2start, call.p2end, enumitem_ambience.Meta))
			{
				call.nparam = 1;
			}

			if (call.nparam == 1 && syntax.is_white_separator(current.meta.arg_start)
				&& is_comment_or_white(call.p1start, call.p1end, enumitem_ambience.Meta))
			{
				call.nparam = 0;
			}

			if (meta.expected_params != 0 && call.nparam == 0)
			{
				throw make_error("Missing argument in meta-macro");
			}

			meta.run(this, call);
		}
		finally
		{
			if (is_mode)
			{
				rem_specs();
			}
		}

		apply_shift(macro_end);

		return true;
	}

	private int get_full_end(metacall call)
	{
		return call.nparam == 2 ? call.p2end : call.p1end;
	}

	private void add_if_level(Func<bool> is_skipped)
	{
		if_level++;

		if (if_level == priv.stack_depth)
		{
			throw make_error("Too many nested #ifs");
		}

		commented[if_level] = commented[if_level - 1];

		if (commented[if_level] == 0)
		{
			commented[if_level] = is_skipped() ? 1 : 0;
		}
	}

	private void add_extra_warning(metacall call, string name)
	{
		if (call.nparam == 2 && options.warning_level >= priv.verbose_warnings)
		{
			add_warning($"Extra argument to {name} ignored");
		}
	}

	private void invoke_define(metacall call, bool evaluate)
	{
		if (is_commented())
		{
			return;
		}

		string what = evaluate ? "#defeval" : "#define";
		int p1start = call.p1start;
		int p1end = call.p1end;

		if (!apply_trim(ref p1start, ref p1end) || !is_identifier(p1start, p1end))
		{
			throw make_error($"{what} requires an identifier (A-Z,a-z,0-9,_ only)");
		}

		int p2start = call.nparam == 1 ? p1end : call.p2start;
		int p2end = call.nparam == 1 ? p1end : call.p2end;
		string text = evaluate
			? invoke_range(p2start, p2end, enumitem_ambience.Meta)
			: get_text_without_comments(p2start, p2end, enumitem_ambience.Meta);
		string name = get_text(p1start, p1end - p1start);
		macros.Remove(name);
		macro m = new macro
		{
			name = name,
			text = text,
			define_specs = current.clone(),
			defined_in_comment = ctx.in_comment,
		};
		int argc = call.named_args ? call.argb.Count : 0;

		for (int j = 0; j < argc; j++)
		{
			int start = call.argb[j];
			int end = call.arge[j];
			apply_trim(ref start, ref end);
			call.argb[j] = start;
			call.arge[j] = end;
		}

		if (argc > 0)
		{
			argc = argc == 1 && call.arge[0] == call.argb[0] ? 0 : argc;
			m.arg_names = new string[argc];
		}

		m.named_arg_count = argc;

		for (int j = 0; j < argc; j++)
		{
			if (!is_identifier(call.argb[j], call.arge[j]))
			{
				throw make_error($"{what} with named args needs identifiers as arg names");
			}

			m.arg_names![j] = get_text(call.argb[j], call.arge[j] - call.argb[j]);
		}

		rebuild_arg_refs(m);
		macros[name] = m;
	}

	private void invoke_undef(metacall call)
	{
		if (is_commented())
		{
			return;
		}

		add_extra_warning(call, "#undef");
		int start = call.p1start;
		int end = call.p1end;

		if (!apply_trim(ref start, ref end) || !is_identifier(start, end))
		{
			throw make_error("#undef requires an identifier (A-Z,a-z,0-9,_ only)");
		}

		macros.Remove(get_text(start, end - start));
	}

	private void invoke_ifdef(metacall call, string name, bool negate)
	{
		add_if_level(() =>
		{
			add_extra_warning(call, name);
			int start = call.p1start;
			int end = call.p1end;

			if (!apply_trim(ref start, ref end) || !is_identifier(start, end))
			{
				throw make_error($"{name} requires an identifier (A-Z,a-z,0-9,_ only)");
			}

			return macros.ContainsKey(get_text(start, end - start)) == negate;
		});
	}

	private void invoke_else(metacall call)
	{
		if (!is_commented() && call.nparam > 0 && options.warning_level >= priv.verbose_warnings)
		{
			add_warning("Extra argument to #else ignored");
		}

		if (if_level == 0)
		{
			throw make_error("#else without #if");
		}

		if (commented[if_level - 1] == 0 && commented[if_level] != 2)
		{
			commented[if_level] = commented[if_level] == 0 ? 1 : 0;
		}
	}

	private void invoke_endif(metacall call)
	{
		if (!is_commented() && call.nparam > 0 && options.warning_level >= priv.verbose_warnings)
		{
			add_warning("Extra argument to #endif ignored");
		}

		if (if_level == 0)
		{
			throw make_error("#endif without #if");
		}

		if_level--;
	}

	private void invoke_include_macro(metacall call, string name, bool is_silent)
	{
		if (is_commented())
		{
			return;
		}

		add_extra_warning(call, name);
		int start = call.p1start;
		int end = call.p1end;

		if (!apply_trim(ref start, ref end))
		{
			throw make_error($"Missing file name in {name}");
		}

		bool is_quoted = get_char(start) == '"' && get_char(end - 1) == '"';
		bool is_bracketed = get_char(start) == '<' && get_char(end - 1) == '>';

		if (is_quoted || is_bracketed)
		{
			start++;
			end--;
		}

		if (start >= end)
		{
			throw make_error($"Missing file name in {name}");
		}

		invoke_include(get_text(start, end - start), is_silent, false);
	}

	private void invoke_exec(metacall call)
	{
		if (is_commented())
		{
			return;
		}

		if (!options.allow_exec)
		{
			add_warning("Not allowed to #exec. Command output will be left blank");

			return;
		}

		string command = invoke_range(call.p1start, call.p1end, enumitem_ambience.Meta);

		if (call.nparam == 2)
		{
			command += " " + invoke_range(call.p2start, call.p2end, enumitem_ambience.Meta);
		}

		add_raw_output(invoke_command(command));
	}

	private void invoke_ifeq(metacall call, string name, bool negate)
	{
		add_if_level(() =>
		{
			if (call.nparam != 2)
			{
				throw make_error($"{name} requires two arguments");
			}

			string left = invoke_range(call.p1start, call.p1end, enumitem_ambience.Meta).Trim(priv.white_chars);
			string right = invoke_range(call.p2start, call.p2end, enumitem_ambience.Meta).Trim(priv.white_chars);

			return string.Equals(left, right, StringComparison.Ordinal) == negate;
		});
	}

	private void invoke_eval(metacall call)
	{
		if (!is_commented())
		{
			add_raw_output(invoke_expression(call.p1start, get_full_end(call)));
		}
	}

	private void invoke_if(metacall call)
	{
		add_if_level(() => invoke_expression(call.p1start, get_full_end(call)) == "0");
	}

	private void invoke_elif(metacall call)
	{
		if (if_level == 0)
		{
			throw make_error("#elif without #if");
		}

		if (commented[if_level - 1] != 0)
		{
			return;
		}

		if (commented[if_level] != 1)
		{
			commented[if_level] = 2;

			return;
		}

		commented[if_level] = 0;
		commented[if_level] = invoke_expression(call.p1start, get_full_end(call)) == "0" ? 1 : 0;
	}

	private void invoke_error(metacall call)
	{
		if (!is_commented())
		{
			throw make_error(invoke_range(call.p1start, get_full_end(call), enumitem_ambience.Meta));
		}
	}

	private void invoke_warning(metacall call)
	{
		if (!is_commented())
		{
			add_warning(invoke_range(call.p1start, get_full_end(call), enumitem_ambience.Meta));
		}
	}

	private void invoke_date(metacall call)
	{
		string format = invoke_range(call.p1start, get_full_end(call), enumitem_ambience.Meta);
		add_output(strftime.to_text(format, DateTime.Now), false);
	}

	private void invoke_mode(metacall call)
	{
		if (!is_commented())
		{
			apply_mode_command(call.p1start, call.p1end, call.nparam == 1 ? -1 : call.p2start, call.p2end);
		}
	}

	private void apply_mode_command(int p1start, int p1end, int p2start, int p2end)
	{
		if (!apply_trim(ref p1start, ref p1end) || !is_identifier(p1start, p1end))
		{
			throw make_error("invalid #mode syntax");
		}

		string keyword = get_text(p1start, p1end - p1start);
		string text = p2start < 0 ? "" : invoke_range(p2start, p2end, enumitem_ambience.Meta);
		specs target = current.next ?? throw make_error("#mode restore without #mode save");
		int pos = 0;

		while (pos < text.Length && syntax.is_white(text[pos]))
		{
			pos++;
		}

		string? option = null;

		if (pos < text.Length && text[pos] != '"')
		{
			int start = pos;

			while (pos < text.Length && !syntax.is_white(text[pos]))
			{
				pos++;
			}

			option = text[start..pos];

			while (pos < text.Length && syntax.is_white(text[pos]))
			{
				pos++;
			}
		}

		List<string> args = [];
		bool check_delim = keyword != "charset";

		while (pos < text.Length)
		{
			if (args.Count == priv.max_mode_args)
			{
				throw make_error("too many arguments in #mode command");
			}

			if (text[pos] != '"')
			{
				throw make_error("syntax error in #mode command (missing \" or trailing data)");
			}

			pos++;
			result<string> arg = syntax.from_c_string(text, ref pos, check_delim);

			if (!arg.state)
			{
				throw make_error(arg.message);
			}

			args.Add(arg.value);

			while (pos < text.Length && syntax.is_white(text[pos]))
			{
				pos++;
			}
		}

		if (!relevant_mode_commands.TryGetValue(keyword, out Action<gppprocessor, modecall>? command))
		{
			throw make_error("unrecognized #mode command");
		}

		command(this, new modecall(option, args, target));
	}

	private static char get_first(string s)
	{
		return s.Length > 0 ? s[0] : '\0';
	}

	private void apply_mode_quote(modecall m)
	{
		if (m.option is not null || m.args.Count > 1)
		{
			throw make_error("syntax error in #mode quote command");
		}

		m.target.user = m.target.user with { quote_char = m.args.Count == 0 ? '\0' : get_first(m.args[0]) };
	}

	private void apply_mode_comment(modecall m, string keyword, string default_modifier)
	{
		if (m.args.Count is < 2 or > 4)
		{
			throw make_error($"syntax error in #mode {keyword} command");
		}

		char quote = m.args.Count > 2 ? get_first(m.args[2]) : '\0';
		char warn = m.args.Count > 3 ? get_first(m.args[3]) : '\0';
		apply_result(m.target.add_comment(m.option ?? default_modifier, m.args[0], m.args[1], quote, warn));
	}

	private void apply_mode_save(modecall m)
	{
		if (m.option is not null || m.args.Count > 0)
		{
			throw make_error("too many arguments to #mode save");
		}

		specs saved = m.target.clone();
		saved.next = m.target;
		current.next = saved;
	}

	private void apply_mode_restore(modecall m)
	{
		if (m.option is not null || m.args.Count > 0)
		{
			throw make_error("too many arguments to #mode restore");
		}

		current.next = m.target.next ?? throw make_error("#mode restore without #mode save");
	}

	private void apply_mode_standard(modecall m)
	{
		if (m.option is null || m.args.Count > 0)
		{
			throw make_error("syntax error in #mode standard");
		}

		apply_result(m.target.apply_standard_mode(m.option));
	}

	private void apply_mode_user(modecall m)
	{
		if (m.option is not null || m.args.Count != 9)
		{
			throw make_error("#mode user requires 9 arguments");
		}

		List<string> a = m.args;
		m.target.user = new macromode(a[0], a[1], a[2], a[3], a[4], a[7], get_first(a[8]), a[5], a[6]);
	}

	private void apply_mode_meta(modecall m)
	{
		if (m.option == "user" && m.args.Count == 0)
		{
			m.target.meta = m.target.user;

			return;
		}

		if (m.option is not null || m.args.Count != 7)
		{
			throw make_error("#mode meta requires 7 arguments");
		}

		List<string> a = m.args;
		m.target.meta = m.target.meta with
		{
			start = a[0],
			end = a[1],
			arg_start = a[2],
			arg_sep = a[3],
			arg_end = a[4],
			stack_chars = a[5],
			unstack_chars = a[6],
		};
	}

	private void apply_mode_preservelf(modecall m)
	{
		if (m.option is null || m.args.Count > 0)
		{
			throw make_error("syntax error in #mode preservelf");
		}

		if (!relevant_switches.TryGetValue(m.option, out bool enabled))
		{
			throw make_error("#mode preservelf requires on/off argument");
		}

		m.target.preserve_lf = enabled;
	}

	private void apply_mode_nocomment(modecall m)
	{
		if (m.option is not null || m.args.Count > 1)
		{
			throw make_error("syntax error in #mode nocomment/nostring");
		}

		if (m.args.Count == 0)
		{
			m.target.comments.Clear();

			return;
		}

		m.target.rem_comment(m.args[0]);
	}

	private void apply_mode_charset(modecall m)
	{
		if (m.option is null || m.args.Count != 1)
		{
			throw make_error("syntax error in #mode charset");
		}

		if (!relevant_charsets.TryGetValue(m.option, out Action<specs, charset>? apply))
		{
			throw make_error("unknown charset subset name in #mode charset");
		}

		result<charset> set = charset.make(m.args[0]);

		if (!set.state)
		{
			throw make_error(set.message);
		}

		apply(m.target, set.value);
	}

	private string? get_include_path(string file_name, string base_directory, bool from_cwd)
	{
		string? in_base = File.Exists(Path.Combine(base_directory, file_name)) ? Path.Combine(base_directory, file_name) : null;

		if (Path.IsPathRooted(file_name))
		{
			return File.Exists(file_name) ? file_name : null;
		}

		if ((!options.no_current_dir_first || from_cwd) && in_base is not null)
		{
			return in_base;
		}

		foreach (string directory in options.include_dirs)
		{
			string candidate = Path.Combine(directory, file_name);

			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		return options.current_dir_last ? in_base : null;
	}

	private void invoke_include(string file_name, bool is_silent, bool from_cwd)
	{
		string base_directory = from_cwd ? Directory.GetCurrentDirectory() : ctx.directory;
		string? path = get_include_path(file_name, base_directory, from_cwd);

		if (path is null)
		{
			if (is_silent)
			{
				return;
			}

			throw make_error($"Requested include file not found: {file_name}");
		}

		string full_path = Path.GetFullPath(path);
		string text;
		on_include?.Invoke(full_path);

		try
		{
			text = read_source(full_path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw make_error($"Cannot read include file {file_name}: {exception.Message}");
		}

		inputcontext parent = ctx;
		ctx = new inputcontext
		{
			buf = "\n" + text,
			file_name = file_name,
			directory = Path.GetDirectoryName(full_path) ?? base_directory,
			is_file = true,
			output = parent.output,
		};
		add_specs(current);
		bool is_c_header = file_name.EndsWith(".h", StringComparison.Ordinal) || file_name.EndsWith(".c", StringComparison.Ordinal);

		if (options.auto_switch_on_c_headers && is_c_header)
		{
			apply_result(current.apply_standard_mode("C"));
		}

		try
		{
			invoke_context();
		}
		finally
		{
			rem_specs();
			ctx = parent;
		}
	}

	private string invoke_command(string command)
	{
		ProcessStartInfo info = OperatingSystem.IsWindows()
			? new ProcessStartInfo("cmd.exe", ["/d", "/s", "/c", command])
			: new ProcessStartInfo("/bin/sh", ["-c", command]);
		info.RedirectStandardOutput = true;
		info.UseShellExecute = false;
		info.WorkingDirectory = ctx.directory;

		try
		{
			using Process? process = Process.Start(info);

			if (process is null)
			{
				add_warning("Cannot #exec. Command not found(?)");

				return "";
			}

			string output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();

			return output.Replace("\r", "");
		}
		catch (Win32Exception)
		{
			add_warning("Cannot #exec. Command not found(?)");

			return "";
		}
	}
}
