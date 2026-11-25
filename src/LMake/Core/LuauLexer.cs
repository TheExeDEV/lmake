using System.Globalization;
using System.Text;

namespace lmake;

file static partial class priv
{
	public const int max_decimal_escape_digits = 3;
	public const int hex_escape_digits = 2;
	public const int hex_base = 16;

	public static readonly string[] relevant_compound_symbols = ["...", "..", "::"];

	public static readonly Dictionary<char, string> relevant_escapes = new()
	{
		['a'] = "\a",
		['b'] = "\b",
		['f'] = "\f",
		['n'] = "\n",
		['r'] = "\r",
		['t'] = "\t",
		['v'] = "\v",
		['\\'] = "\\",
		['"'] = "\"",
		['\''] = "'",
		['\n'] = "\n",
	};
}

internal sealed class luauscanner(string text)
{
	public readonly List<tokeninfo> tokens = [];

	private int pos;

	public void read_all()
	{
		read_code(false);
	}

	private char get_char(int index)
	{
		return index < text.Length ? text[index] : '\0';
	}

	private void add_token(enumitem_token kind, int start)
	{
		tokens.Add(new tokeninfo(kind, start, pos - start, text[start..pos]));
	}

	private void read_code(bool until_brace)
	{
		int depth = 0;

		while (pos < text.Length)
		{
			char c = text[pos];

			if (char.IsWhiteSpace(c))
			{
				pos++;
				continue;
			}

			if (c == '-' && get_char(pos + 1) == '-')
			{
				read_comment();
				continue;
			}

			if (until_brace && c == '{')
			{
				depth++;
			}

			if (until_brace && c == '}')
			{
				if (depth == 0)
				{
					return;
				}

				depth--;
			}

			int start = pos;

			if (char.IsLetter(c) || c == '_')
			{
				read_while(ch => char.IsLetterOrDigit(ch) || ch == '_');
				add_token(enumitem_token.Name, start);
			}
			else if (char.IsDigit(c) || (c == '.' && char.IsDigit(get_char(pos + 1))))
			{
				read_number();
				add_token(enumitem_token.Number, start);
			}
			else if (c is '"' or '\'')
			{
				read_quoted(c);
				add_token(enumitem_token.String, start);
			}
			else if (c == '[' && get_long_level(pos) >= 0)
			{
				read_long(get_long_level(pos));
				add_token(enumitem_token.LongString, start);
			}
			else if (c == '`')
			{
				read_interpolated();
			}
			else
			{
				read_symbol();
			}
		}
	}

	private void read_while(Func<char, bool> predicate)
	{
		while (pos < text.Length && predicate(text[pos]))
		{
			pos++;
		}
	}

	private void read_number()
	{
		bool is_hex = get_char(pos) == '0' && get_char(pos + 1) is 'x' or 'X';

		while (pos < text.Length)
		{
			char c = text[pos];
			bool is_exponent_sign = !is_hex && c is '+' or '-' && text[pos - 1] is 'e' or 'E';

			if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && !is_exponent_sign)
			{
				return;
			}

			pos++;
		}
	}

	private void read_symbol()
	{
		int start = pos;

		foreach (string symbol in priv.relevant_compound_symbols)
		{
			if (string.CompareOrdinal(text, pos, symbol, 0, symbol.Length) == 0)
			{
				pos += symbol.Length;
				add_token(enumitem_token.Symbol, start);

				return;
			}
		}

		pos++;
		add_token(enumitem_token.Symbol, start);
	}

	private void read_comment()
	{
		pos += 2;

		if (get_char(pos) == '[' && get_long_level(pos) >= 0)
		{
			read_long(get_long_level(pos));

			return;
		}

		read_while(ch => ch != '\n');
	}

	private int get_long_level(int at)
	{
		int i = at + 1;

		while (get_char(i) == '=')
		{
			i++;
		}

		return get_char(i) == '[' ? i - at - 1 : -1;
	}

	private void read_long(int level)
	{
		string close = "]" + new string('=', level) + "]";
		int end = text.IndexOf(close, pos + level + 2, StringComparison.Ordinal);
		pos = end < 0 ? text.Length : end + close.Length;
	}

	private void read_quoted(char quote)
	{
		pos++;

		while (pos < text.Length && text[pos] != quote && text[pos] != '\n')
		{
			if (text[pos] != '\\')
			{
				pos++;
				continue;
			}

			bool skips_white = get_char(pos + 1) == 'z';
			pos += 2;

			if (skips_white)
			{
				read_while(char.IsWhiteSpace);
			}
		}

		pos = Math.Min(pos + 1, text.Length);
	}

	private void read_interpolated()
	{
		int start = pos;
		pos++;

		while (pos < text.Length && text[pos] != '`')
		{
			if (text[pos] == '\\')
			{
				pos += 2;
				continue;
			}

			if (text[pos] != '{')
			{
				pos++;
				continue;
			}

			add_token(enumitem_token.InterpolatedString, start);
			pos++;
			read_code(true);
			start = pos;
			pos++;
		}

		pos = Math.Min(pos + 1, text.Length);
		add_token(enumitem_token.InterpolatedString, start);
	}
}

public static partial class luaulexer
{
	public static partial IReadOnlyList<tokeninfo> read_tokens(string text)
	{
		luauscanner scanner = new luauscanner(text);
		scanner.read_all();

		return scanner.tokens;
	}

	public static partial result<string> read_string_value(string text, tokeninfo token)
	{
		if (token.kind == enumitem_token.LongString)
		{
			int open = token.value.IndexOf('[', 1) + 1;
			int level = open - 2;
			string content = token.value[open..^(level + 2)];

			return result<string>.ok(content.StartsWith('\n') ? content[1..] : content);
		}

		if (token.kind != enumitem_token.String || token.length < 2)
		{
			return result<string>.fail("not a string literal");
		}

		return read_escaped(token.value[1..^1]);
	}

	private static result<string> read_escaped(string body)
	{
		StringBuilder value = new StringBuilder(body.Length);

		for (int i = 0; i < body.Length; i++)
		{
			if (body[i] != '\\')
			{
				value.Append(body[i]);
				continue;
			}

			i++;

			if (i >= body.Length)
			{
				return result<string>.fail("unfinished escape sequence");
			}

			char key = body[i];

			if (priv.relevant_escapes.TryGetValue(key, out string? escaped))
			{
				value.Append(escaped);
			}
			else if (key == 'z')
			{
				while (i + 1 < body.Length && char.IsWhiteSpace(body[i + 1]))
				{
					i++;
				}
			}
			else if (char.IsDigit(key))
			{
				int digits = 1;

				while (digits < priv.max_decimal_escape_digits && i + digits < body.Length && char.IsDigit(body[i + digits]))
				{
					digits++;
				}

				value.Append((char)int.Parse(body.AsSpan(i, digits), CultureInfo.InvariantCulture));
				i += digits - 1;
			}
			else if (key == 'x' && i + priv.hex_escape_digits < body.Length)
			{
				value.Append((char)Convert.ToInt32(body.Substring(i + 1, priv.hex_escape_digits), priv.hex_base));
				i += priv.hex_escape_digits;
			}
			else if (key == 'u' && i + 1 < body.Length && body[i + 1] == '{' && body.IndexOf('}', i) > i)
			{
				int close = body.IndexOf('}', i);
				int code = Convert.ToInt32(body[(i + 2)..close], priv.hex_base);
				value.Append(char.ConvertFromUtf32(code));
				i = close;
			}
			else
			{
				return result<string>.fail($"invalid escape sequence '\\{key}'");
			}
		}

		return result<string>.ok(value.ToString());
	}
}
