namespace lmake;

file static partial class priv
{
	public static readonly string blanks = syntax.make_special(syntax.one_or_more_blanks).ToString();
	public static readonly string optional_blanks = syntax.make_special(syntax.zero_or_more_blanks).ToString();
	public static readonly string white = syntax.make_special(syntax.one_or_more_white).ToString();

	public static readonly Dictionary<char, enumitem_comment_flags> relevant_modifiers = new()
	{
		['I'] = enumitem_comment_flags.Ignore,
		['i'] = enumitem_comment_flags.Ignore,
		['c'] = enumitem_comment_flags.Comment,
		['s'] = enumitem_comment_flags.String,
		['q'] = enumitem_comment_flags.OutputText,
		['S'] = enumitem_comment_flags.String | enumitem_comment_flags.ParseMacros,
		['Q'] = enumitem_comment_flags.OutputText | enumitem_comment_flags.ParseMacros,
		['C'] = enumitem_comment_flags.Comment | enumitem_comment_flags.ParseMacros,
	};

	public static readonly Dictionary<string, Action<specs>> relevant_standard_modes = new()
	{
		["C"] = apply_cpp,
		["cpp"] = apply_cpp,
		["TeX"] = apply_tex,
		["tex"] = apply_tex,
		["HTML"] = apply_html,
		["html"] = apply_html,
		["XHTML"] = apply_xhtml,
		["xhtml"] = apply_xhtml,
		["default"] = apply_default,
		["Prolog"] = apply_prolog,
		["prolog"] = apply_prolog,
	};
}

internal enum enumitem_ambience
{
	Meta = 0,
	User = 1,
	Text = 2,
}

[Flags]
internal enum enumitem_comment_flags
{
	Comment = 0,
	OutputText = 0x1,
	OutputDelim = 0x2,
	ParseMacros = 0x4,
	Ignore = 0x40,
	String = OutputText | OutputDelim,
}

internal sealed record macromode(
	string start,
	string end,
	string arg_start,
	string arg_sep,
	string arg_end,
	string arg_ref,
	char quote_char,
	string stack_chars,
	string unstack_chars)
{
	public static readonly macromode c_user = new macromode("", "", "(", ",", ")", "#", '\\', "(", ")");
	public static readonly macromode c_meta = new macromode("#", "\n", priv.blanks, priv.blanks, "\n", "#", '\\', "(", ")");
	public static readonly macromode k_user = new macromode("", "", "(", ",", ")", "#", '\0', "(", ")");
	public static readonly macromode k_meta =
		new macromode("\n#" + priv.optional_blanks, "\n", priv.blanks, priv.blanks, "\n", "#", '\0', "", "");
	public static readonly macromode tex = new macromode("\\", "", "{", "}{", "}", "#", '@', "{", "}");
	public static readonly macromode html = new macromode("<#", ">", priv.white, "|", ">", "#", '\\', "<", ">");
	public static readonly macromode xhtml = new macromode("<#", "/>", priv.white, "|", "/>", "#", '\\', "<", ">");
}

internal sealed record commentspec(string start, string end, char quote, char warn, enumitem_comment_flags[] flags)
{
	public enumitem_comment_flags get_flags(enumitem_ambience ambience)
	{
		return flags[(int)ambience];
	}
}

internal sealed class specs
{
	private const int modifier_length = 3;

	public macromode user = macromode.c_user;
	public macromode meta = macromode.c_meta;
	public List<commentspec> comments = [];
	public specs? next;
	public bool preserve_lf;
	public charset op_set = charset.default_operators;
	public charset ext_op_set = charset.default_parens;
	public charset id_set = charset.default_ident;

	public specs clone()
	{
		return new specs
		{
			user = user,
			meta = meta,
			comments = [.. comments],
			next = null,
			preserve_lf = preserve_lf,
			op_set = op_set,
			ext_op_set = ext_op_set,
			id_set = id_set,
		};
	}

	public result<nothing> add_comment(string modifier, string start, string end, char quote, char warn)
	{
		if (start.Length == 0)
		{
			return result<nothing>.fail("Comment/string start delimiter must be non-empty");
		}

		if (modifier.Length != modifier_length || !modifier.All(priv.relevant_modifiers.ContainsKey))
		{
			return result<nothing>.fail("Invalid comment/string modifier");
		}

		enumitem_comment_flags[] flags = [.. modifier.Select(c => priv.relevant_modifiers[c])];
		commentspec spec = new commentspec(start, end, quote, warn, flags);
		int index = comments.FindIndex(c => c.start == start);

		if (index < 0)
		{
			comments.Insert(0, spec);

			return result<nothing>.ok(new nothing());
		}

		if (comments[index].end != end)
		{
			return result<nothing>.fail("Conflicting comment/string delimiter specifications");
		}

		comments[index] = spec;

		return result<nothing>.ok(new nothing());
	}

	public void rem_comment(string start)
	{
		int index = comments.FindIndex(c => c.start == start);

		if (index >= 0)
		{
			comments.RemoveAt(index);
		}
	}

	public result<nothing> apply_standard_mode(string name)
	{
		if (!priv.relevant_standard_modes.TryGetValue(name, out Action<specs>? apply))
		{
			return result<nothing>.fail("unknown standard mode");
		}

		op_set = charset.default_operators;
		ext_op_set = charset.default_parens;
		id_set = charset.default_ident;
		comments.Clear();
		apply(this);

		return result<nothing>.ok(new nothing());
	}

	public void add_cpp_comments()
	{
		add_comment("ccc", "/*", "*/", '\0', '\0');
		add_comment("ccc", "//", "\n", '\0', '\0');
		add_comment("ccc", "\\\n", "", '\0', '\0');
		add_comment("sss", "\"", "\"", '\\', '\n');
		add_comment("sss", "'", "'", '\\', '\n');
	}

	public void add_prolog_comments()
	{
		add_comment("css", syntax.make_special(syntax.operator_char, true) + "/*", "*/", '\0', '\0');
		add_comment("cii", "\\\n", "", '\0', '\0');
		add_comment("css", "%", "\n", '\0', '\0');
		add_comment("sss", "\"", "\"", '\0', '\n');
		add_comment("sss", syntax.make_special(syntax.digit, true) + "'", "'", '\0', '\n');
	}
}

internal sealed class macro
{
	public const int named_args_from_refs = 0;
	public const int no_arg_refs = -1;
	public const int defined_operator = -2;

	public required string name;
	public string text = "";
	public string[]? arg_names;
	public int named_arg_count;
	public specs? define_specs;
	public bool defined_in_comment;
}

file static partial class priv
{
	public static void apply_cpp(specs s)
	{
		s.user = macromode.k_user;
		s.meta = macromode.k_meta;
		s.preserve_lf = true;
		s.add_cpp_comments();
	}

	public static void apply_tex(specs s)
	{
		s.user = macromode.tex;
		s.meta = macromode.tex;
		s.preserve_lf = false;
	}

	public static void apply_html(specs s)
	{
		s.user = macromode.html;
		s.meta = macromode.html;
		s.preserve_lf = false;
	}

	public static void apply_xhtml(specs s)
	{
		s.user = macromode.xhtml;
		s.meta = macromode.xhtml;
		s.preserve_lf = false;
	}

	public static void apply_default(specs s)
	{
		s.user = macromode.c_user;
		s.meta = macromode.c_meta;
		s.preserve_lf = false;
	}

	public static void apply_prolog(specs s)
	{
		s.user = macromode.k_user;
		s.meta = macromode.k_meta;
		s.preserve_lf = true;
		s.op_set = charset.prolog_operators;
		s.add_prolog_comments();
	}
}
