namespace lmake;

file static partial class priv
{
	public const int user_mode_arg_count = 9;
	public const int meta_mode_arg_count = 7;

	public delegate result<nothing> handler(optionparser parser, string arg);

	public static readonly Dictionary<string, handler> relevant_long_options = new()
	{
		["--help"] = (p, a) => p.set(o => o.show_help = true),
		["-h"] = (p, a) => p.set(o => o.show_help = true),
		["--version"] = (p, a) => p.set(o => o.show_version = true),
		["--include"] = (p, a) => p.read_next(a, v => p.pre_includes.Add(v)),
		["--nostdinc"] = (p, a) => result<nothing>.ok(new nothing()),
		["-nostdinc"] = (p, a) => result<nothing>.ok(new nothing()),
		["--nocurinc"] = (p, a) => p.set(o => o.no_current_dir_first = true),
		["-nocurinc"] = (p, a) => p.set(o => o.no_current_dir_first = true),
		["--curdirinclast"] = (p, a) => p.set(o => o.current_dir_last = o.no_current_dir_first = true),
		["-curdirinclast"] = (p, a) => p.set(o => o.current_dir_last = o.no_current_dir_first = true),
		["--warninglevel"] = read_warning_level,
		["-warninglevel"] = read_warning_level,
		["--includemarker"] = (p, a) => result<nothing>.fail("--includemarker is not supported by the built-in gpp"),
		["-includemarker"] = (p, a) => result<nothing>.fail("--includemarker is not supported by the built-in gpp"),
	};

	public static readonly Dictionary<char, handler> relevant_plus_options = new()
	{
		['c'] = read_comment,
		['s'] = read_string,
		['z'] = (p, a) => p.set(o => o.dos_line_endings = false),
		['n'] = (p, a) => p.set_specs(s => s.preserve_lf = false),
	};

	public static readonly Dictionary<char, handler> relevant_minus_options = new()
	{
		['I'] = (p, a) => p.read_attached_or_next(a, v => p.include_dirs.Add(v)),
		['C'] = (p, a) => p.apply_exclusive_mode(a, apply_cpp_mode),
		['P'] = (p, a) => p.apply_exclusive_mode(a, apply_prolog_mode),
		['T'] = (p, a) => p.apply_exclusive_mode(a, s => s.user = s.meta = macromode.tex),
		['H'] = (p, a) => p.apply_exclusive_mode(a, s => s.user = s.meta = macromode.html),
		['X'] = (p, a) => p.apply_exclusive_mode(a, s => s.user = s.meta = macromode.xhtml),
		['U'] = read_user_mode,
		['M'] = read_meta_mode,
		['o'] = read_output,
		['O'] = read_output,
		['D'] = (p, a) => p.read_attached_or_next(a, v => p.defines.Add(syntax.to_define_text(v))),
		['x'] = (p, a) => p.set(o => o.allow_exec = true),
		['n'] = (p, a) => p.set_specs(s => s.preserve_lf = true),
		['z'] = (p, a) => p.set(o => o.dos_line_endings = true),
		['c'] = rem_comment,
		['s'] = rem_comment,
		['m'] = (p, a) => p.set(o => o.auto_switch_on_c_headers = true),
	};
}

internal sealed class optionparser(IReadOnlyList<string> args, gppoptions options, specs specs)
{
	public readonly List<string> defines = [];
	public readonly List<string> include_dirs = [];
	public readonly List<string> pre_includes = [];
	public readonly gppoptions options = options;
	public readonly specs specs = specs;
	public int index;
	public bool has_input;
	public bool has_output;
	public bool has_mode;
	public bool has_meta;
	public bool has_user;

	public result<nothing> set(Action<gppoptions> apply)
	{
		apply(options);

		return result<nothing>.ok(new nothing());
	}

	public result<nothing> set_specs(Action<specs> apply)
	{
		apply(specs);

		return result<nothing>.ok(new nothing());
	}

	public result<string> read_value(string arg)
	{
		if (index + 1 >= args.Count)
		{
			return result<string>.fail($"missing argument after '{arg}'");
		}

		index++;

		return result<string>.ok(args[index]);
	}

	public result<nothing> read_next(string arg, Action<string> apply)
	{
		result<string> value = read_value(arg);

		if (!value.state)
		{
			return result<nothing>.fail(value.message);
		}

		apply(value.value);

		return result<nothing>.ok(new nothing());
	}

	public result<nothing> read_attached_or_next(string arg, Action<string> apply)
	{
		if (arg.Length > 2)
		{
			apply(arg[2..]);

			return result<nothing>.ok(new nothing());
		}

		return read_next(arg, apply);
	}

	public result<nothing> apply_exclusive_mode(string arg, Action<specs> apply)
	{
		if (has_mode || has_meta || has_user)
		{
			return result<nothing>.fail($"'{arg}' conflicts with another mode switch");
		}

		has_mode = true;
		apply(specs);

		return result<nothing>.ok(new nothing());
	}

	public result<string[]> read_encoded(string arg, int count)
	{
		string[] values = new string[count];

		for (int i = 0; i < count; i++)
		{
			result<string> value = read_value(arg);

			if (!value.state)
			{
				return result<string[]>.fail(value.message);
			}

			values[i] = value.value;
		}

		return result<string[]>.ok(values);
	}

	public result<nothing> set_input(string name)
	{
		if (has_input)
		{
			return result<nothing>.fail("only one input file may be given");
		}

		has_input = true;

		return set(o => o.input_file = name);
	}
}

public sealed partial class gppoptions
{
	private specs initial_specs = new specs();

	internal specs get_initial_specs()
	{
		return initial_specs.clone();
	}

	public static partial result<gppoptions> from_args(IReadOnlyList<string> args)
	{
		gppoptions options = new gppoptions { warning_level = project.gpp_warning_level };
		optionparser parser = new optionparser(args, options, options.initial_specs);
		bool end_of_options = false;

		for (parser.index = 0; parser.index < args.Count; parser.index++)
		{
			string arg = args[parser.index];
			result<nothing> handled = priv.handle(parser, arg, ref end_of_options);

			if (!handled.state)
			{
				return result<gppoptions>.fail(handled.message);
			}
		}

		options.defines = parser.defines;
		options.include_dirs = parser.include_dirs;
		options.pre_includes = parser.pre_includes;

		return result<gppoptions>.ok(options);
	}

	public static partial string get_usage()
	{
		return $$"""
			Usage : {{project.name}} gpp [-{o|O} outfile] [-I/include/path] [-Dname=val ...] [-z] [-x] [-m]
			                [-n] [-C | -T | -H | -X | -P | -U ... [-M ...]] [+c<n> str1 str2]
			                [+s<n> str1 str2 c] [long options] [infile]

			      default:    #define x y           macro(arg,...)
			 -C : maximum cpp compatibility (includes -n, +c, +s, ...)
			 -T : TeX-like    \define{x}{y}         \macro{arg}{...}
			 -H : HTML-like   <#define x|y>         <#macro arg|...>
			 -X : XHTML-like  <#define x|y/>        <#macro arg|.../>
			 -P : prolog compatible cpp-like mode
			 -U : user-defined syntax (specified in 9 following args; see manual)
			 -M : user-defined syntax for meta-macros (specified in 7 following args)

			 -o : output to outfile
			 -O : output to outfile and stdout
			 -z : line terminator is CR-LF (MS-DOS style)
			 -x : enable #exec built-in macro
			 -m : enable automatic mode switching upon including .h/.c files
			 -n : send LF characters serving as macro terminators to output
			 +c : use next 2 args as comment start and comment end sequences
			 +s : use next 3 args as string start, end and quote character

			 Long options:
			 --include file : process file before infile
			 --nostdinc : don't search standard directories for files to include
			 --nocurinc : don't search the current directory for files to include
			 --curdirinclast : search the current directory last
			 --warninglevel n : set warning level

			 --version : display version information and exit
			 -h, --help : display this message and exit
			""";
	}
}

file static partial class priv
{
	public static result<nothing> handle(optionparser parser, string arg, ref bool end_of_options)
	{
		if (!end_of_options && arg == "--")
		{
			end_of_options = true;

			return result<nothing>.ok(new nothing());
		}

		if (end_of_options)
		{
			return parser.set_input(arg);
		}

		if (relevant_long_options.TryGetValue(arg, out handler? long_handler))
		{
			return long_handler(parser, arg);
		}

		if (arg.Length > 1 && arg[0] == '+')
		{
			return relevant_plus_options.TryGetValue(arg[1], out handler? plus_handler)
				? plus_handler(parser, arg)
				: result<nothing>.fail($"unknown option '{arg}'");
		}

		if (arg.Length < 2 || arg[0] != '-')
		{
			return parser.set_input(arg);
		}

		return relevant_minus_options.TryGetValue(arg[1], out handler? minus_handler)
			? minus_handler(parser, arg)
			: result<nothing>.fail($"unknown option '{arg}'");
	}

	public static result<nothing> read_warning_level(optionparser parser, string arg)
	{
		return parser.read_next(arg, v => parser.options.warning_level = int.TryParse(v, out int level) ? level : 0);
	}

	public static result<nothing> read_comment(optionparser parser, string arg)
	{
		string modifier = arg.Length > 2 ? arg[2..] : "ccc";
		result<string[]> values = parser.read_encoded(arg, 2);

		return values.state ? add_comment(parser, modifier, values.value[0], values.value[1], "") : result<nothing>.fail(values.message);
	}

	public static result<nothing> read_string(optionparser parser, string arg)
	{
		string modifier = arg.Length > 2 ? arg[2..] : "sss";
		result<string[]> values = parser.read_encoded(arg, 3);

		return values.state
			? add_comment(parser, modifier, values.value[0], values.value[1], values.value[2])
			: result<nothing>.fail(values.message);
	}

	public static result<nothing> add_comment(optionparser parser, string modifier, string start, string end, string quote)
	{
		result<string> encoded_start = syntax.from_command_line(start);
		result<string> encoded_end = syntax.from_command_line(end);

		if (!encoded_start.state || !encoded_end.state)
		{
			return result<nothing>.fail(encoded_start.state ? encoded_end.message : encoded_start.message);
		}

		return parser.specs.add_comment(modifier, encoded_start.value, encoded_end.value, quote.Length > 0 ? quote[0] : '\0', '\0');
	}

	public static result<nothing> rem_comment(optionparser parser, string arg)
	{
		result<string> value = parser.read_value(arg);

		if (!value.state)
		{
			return result<nothing>.fail(value.message);
		}

		result<string> encoded = syntax.from_command_line(value.value);

		if (!encoded.state)
		{
			return result<nothing>.fail(encoded.message);
		}

		parser.specs.rem_comment(encoded.value);

		return result<nothing>.ok(new nothing());
	}

	public static void apply_cpp_mode(specs s)
	{
		s.user = macromode.k_user;
		s.meta = macromode.k_meta;
		s.preserve_lf = true;
		s.add_cpp_comments();
	}

	public static void apply_prolog_mode(specs s)
	{
		s.user = macromode.k_user;
		s.meta = macromode.k_meta;
		s.preserve_lf = true;
		s.op_set = charset.prolog_operators;
		s.add_prolog_comments();
	}

	public static result<string[]> read_mode(optionparser parser, string arg, int count)
	{
		result<string[]> values = parser.read_encoded(arg, count);

		if (!values.state)
		{
			return values;
		}

		string[] encoded = new string[count];

		for (int i = 0; i < count; i++)
		{
			bool is_quote = count == user_mode_arg_count && i == count - 1;
			result<string> value = is_quote ? result<string>.ok(values.value[i]) : syntax.from_command_line(values.value[i]);

			if (!value.state)
			{
				return result<string[]>.fail(value.message);
			}

			encoded[i] = value.value;
		}

		return result<string[]>.ok(encoded);
	}

	public static result<nothing> read_user_mode(optionparser parser, string arg)
	{
		if (parser.has_mode || parser.has_user)
		{
			return result<nothing>.fail("'-U' conflicts with another mode switch");
		}

		parser.has_user = true;
		result<string[]> values = read_mode(parser, arg, user_mode_arg_count);

		if (!values.state)
		{
			return result<nothing>.fail(values.message);
		}

		string[] m = values.value;
		parser.specs.user = new macromode(m[0], m[1], m[2], m[3], m[4], m[7], m[8].Length > 0 ? m[8][0] : '\0', m[5], m[6]);

		if (!parser.has_meta)
		{
			parser.specs.meta = parser.specs.user;
		}

		return result<nothing>.ok(new nothing());
	}

	public static result<nothing> read_meta_mode(optionparser parser, string arg)
	{
		if (parser.has_mode || parser.has_meta)
		{
			return result<nothing>.fail("'-M' conflicts with another mode switch");
		}

		if (!parser.has_user)
		{
			return result<nothing>.fail("'-M' can only be used after '-U'");
		}

		parser.has_meta = true;
		result<string[]> values = read_mode(parser, arg, meta_mode_arg_count);

		if (!values.state)
		{
			return result<nothing>.fail(values.message);
		}

		string[] m = values.value;
		parser.specs.meta = parser.specs.meta with
		{
			start = m[0],
			end = m[1],
			arg_start = m[2],
			arg_sep = m[3],
			arg_end = m[4],
			stack_chars = m[5],
			unstack_chars = m[6],
		};

		return result<nothing>.ok(new nothing());
	}

	public static result<nothing> read_output(optionparser parser, string arg)
	{
		if (parser.has_output)
		{
			return result<nothing>.fail("only one output file may be given");
		}

		parser.has_output = true;
		bool also_stdout = arg[1] == 'O';

		return parser.read_attached_or_next(arg, v =>
		{
			parser.options.output_file = v;
			parser.options.also_stdout = also_stdout;
		});
	}
}
