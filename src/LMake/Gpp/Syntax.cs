using System.Text;

namespace lmake;

file static partial class priv
{
	public const char base_char = '';
	public const int negate_bit = 0x80;

	public static readonly Dictionary<char, int> relevant_escapes = new()
	{
		['b'] = syntax.one_or_more_blanks,
		['w'] = syntax.zero_or_more_blanks,
		['B'] = syntax.one_or_more_white,
		['W'] = syntax.zero_or_more_white,
		['a'] = syntax.alpha,
		['A'] = syntax.alpha_or_white,
		['#'] = syntax.digit,
		['i'] = syntax.ident,
		['o'] = syntax.operator_char,
		['O'] = syntax.operator_or_paren,
	};

	public static readonly HashSet<int> relevant_unnegatable = [syntax.zero_or_more_blanks, syntax.zero_or_more_white];

	public static readonly HashSet<int> relevant_white_matchers =
	[
		syntax.one_or_more_blanks,
		syntax.zero_or_more_blanks,
		syntax.one_or_more_white,
		syntax.zero_or_more_white,
	];

	public static readonly HashSet<int> relevant_charset_forbidden =
	[
		syntax.zero_or_more_blanks,
		syntax.zero_or_more_white,
		syntax.ident,
		syntax.operator_char,
		syntax.operator_or_paren,
	];

	public static readonly Dictionary<int, string> relevant_charset_members = new()
	{
		[syntax.one_or_more_white] = "\n \t",
		[syntax.one_or_more_blanks] = " \t",
		[syntax.tab] = "\t",
		[syntax.newline] = "\n",
		[syntax.alpha_or_white] = "\n \tABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz",
		[syntax.alpha] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz",
		[syntax.digit] = "0123456789",
	};

	public static readonly Dictionary<int, string> relevant_literal_forms = new()
	{
		[syntax.one_or_more_blanks] = " ",
		[syntax.one_or_more_white] = " ",
	};
}

internal static class syntax
{
	public const int one_or_more_blanks = 1;
	public const int zero_or_more_blanks = 2;
	public const int one_or_more_white = 3;
	public const int zero_or_more_white = 4;
	public const int alpha = 5;
	public const int alpha_or_white = 6;
	public const int digit = 7;
	public const int ident = 8;
	public const int tab = 9;
	public const int newline = 10;
	public const int operator_char = 11;
	public const int operator_or_paren = 12;

	public static char make_special(int code, bool negated = false)
	{
		return (char)(priv.base_char + code + (negated ? priv.negate_bit : 0));
	}

	public static bool is_special(char c)
	{
		if (c == '\n' || c == '\t')
		{
			return true;
		}

		if (c <= priv.base_char || c > priv.base_char + priv.negate_bit + operator_or_paren)
		{
			return false;
		}

		int code = (c - priv.base_char) & ~priv.negate_bit;

		return code is >= one_or_more_blanks and <= operator_or_paren;
	}

	public static bool is_encoded(char c)
	{
		return c != '\n' && c != '\t' && is_special(c);
	}

	public static int get_code(char c)
	{
		if (c == '\n')
		{
			return newline;
		}

		if (c == '\t')
		{
			return tab;
		}

		return (c - priv.base_char) & ~priv.negate_bit;
	}

	public static bool is_negated(char c)
	{
		return is_encoded(c) && ((c - priv.base_char) & priv.negate_bit) != 0;
	}

	public static bool is_delim(char c)
	{
		if (c >= 128)
		{
			return false;
		}

		return !(c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_');
	}

	public static bool is_white(char c)
	{
		return c is ' ' or '\t' or '\n';
	}

	public static bool is_white_separator(string s)
	{
		foreach (char c in s)
		{
			if (is_white(c))
			{
				continue;
			}

			if (!is_encoded(c) || is_negated(c) || !priv.relevant_white_matchers.Contains(get_code(c)))
			{
				return false;
			}
		}

		return true;
	}

	public static string to_define_text(string s)
	{
		return s.Replace("\\n", "\n");
	}

	public static result<string> from_command_line(string s)
	{
		if (s.Length > 0 && !is_delim(s[0]))
		{
			return result<string>.fail("character not allowed to start a syntax specifier");
		}

		StringBuilder encoded = new StringBuilder(s.Length);

		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];

			if (c != '\\')
			{
				encoded.Append(c == ' ' ? make_special(one_or_more_blanks) : c);
				continue;
			}

			bool negated = i + 1 < s.Length && s[i + 1] == '!';
			int key_index = i + 1 + (negated ? 1 : 0);
			char key = key_index < s.Length ? s[key_index] : '\0';
			result<char> escape = get_escape(key, negated);

			if (!escape.state && escape.message.Length > 0)
			{
				return result<string>.fail(escape.message);
			}

			if (!escape.state)
			{
				encoded.Append('\\');
				continue;
			}

			encoded.Append(escape.value);
			i = key_index;
		}

		return result<string>.ok(encoded.ToString());
	}

	public static result<string> from_c_string(string s, ref int index, bool check_delim)
	{
		if (check_delim && index < s.Length && s[index] != '"' && !is_delim(s[index]))
		{
			return result<string>.fail("character not allowed to start a syntax specifier");
		}

		StringBuilder encoded = new StringBuilder();

		while (true)
		{
			if (index >= s.Length)
			{
				return result<string>.fail("unterminated string in #mode command");
			}

			char c = s[index];

			if (c == '"')
			{
				index++;

				return result<string>.ok(encoded.ToString());
			}

			if (c != '\\')
			{
				encoded.Append(c == ' ' ? make_special(one_or_more_blanks) : c);
				index++;
				continue;
			}

			bool negated = index + 1 < s.Length && s[index + 1] == '!';
			int key_index = index + 1 + (negated ? 1 : 0);
			char key = key_index < s.Length ? s[key_index] : '\0';
			result<char> escape = get_escape(key, negated);

			if (escape.state)
			{
				encoded.Append(escape.value);
			}
			else if (escape.message.Length == 0 && !negated && key is '"' or '\\')
			{
				encoded.Append(key);
			}
			else
			{
				return result<string>.fail(escape.message.Length > 0 ? escape.message : "unknown escape sequence in syntax specifier");
			}

			index = key_index + 1;
		}
	}

	public static string to_literal(string s)
	{
		StringBuilder text = new StringBuilder(s.Length);

		foreach (char c in s)
		{
			if (!is_encoded(c))
			{
				text.Append(c);
				continue;
			}

			if (!is_negated(c) && priv.relevant_literal_forms.TryGetValue(get_code(c), out string? literal))
			{
				text.Append(literal);
			}
		}

		return text.ToString();
	}

	private static result<char> get_escape(char key, bool negated)
	{
		if (key is 'n' or 'r')
		{
			return result<char>.ok(negated ? make_special(newline, true) : '\n');
		}

		if (key == 't')
		{
			return result<char>.ok(negated ? make_special(tab, true) : '\t');
		}

		if (!priv.relevant_escapes.TryGetValue(key, out int code))
		{
			return result<char>.fail("");
		}

		if (negated && priv.relevant_unnegatable.Contains(code))
		{
			return result<char>.fail("\\w and \\W cannot be negated");
		}

		return result<char>.ok(make_special(code, negated));
	}
}

internal sealed class charset
{
	public static readonly charset default_operators = make_from_plain("+-*/\\^<>=`~:.?@#&!%|");
	public static readonly charset prolog_operators = make_from_plain("+-*/\\^<>=`~:.?@#&");
	public static readonly charset default_parens = make_from_plain("()[]{}");
	public static readonly charset default_ident = make_from_plain(
		new string([syntax.make_special(syntax.alpha), syntax.make_special(syntax.digit), '_']));

	private const int size = 256;

	private readonly bool[] bits = new bool[size];

	public bool has(char c)
	{
		return c < size && bits[c];
	}

	public static result<charset> make(string s)
	{
		charset set = new charset();

		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];

			if (syntax.is_encoded(c))
			{
				if (syntax.is_negated(c))
				{
					return result<charset>.fail("negated special sequences not allowed in charset specifications");
				}

				int code = syntax.get_code(c);

				if (priv.relevant_charset_forbidden.Contains(code))
				{
					return result<charset>.fail("special sequence not allowed in charset specification");
				}

				set.add_all(priv.relevant_charset_members.GetValueOrDefault(code, ""));
				continue;
			}

			if (i + 2 < s.Length && s[i + 1] == '-' && !syntax.is_encoded(s[i + 2]) && s[i + 2] >= c)
			{
				for (int k = c; k <= s[i + 2]; k++)
				{
					set.add((char)k);
				}

				i += 2;
				continue;
			}

			set.add(c);
		}

		return result<charset>.ok(set);
	}

	private static charset make_from_plain(string s)
	{
		return make(s).value;
	}

	private void add(char c)
	{
		if (c < size)
		{
			bits[c] = true;
		}
	}

	private void add_all(string chars)
	{
		foreach (char c in chars)
		{
			add(c);
		}
	}
}
