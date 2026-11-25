using System.Globalization;

namespace lmake;

file static partial class priv
{
	public const string length_call = "length(";
	public const string length_empty = "length()";
	public const int hex_base = 16;
	public const int octal_base = 8;
	public const int decimal_base = 10;

	public static readonly binaryop[] relevant_logic_ops =
	[
		new binaryop("||", (a, b) => a != 0 || b != 0 ? 1 : 0, false, false),
		new binaryop("&&", (a, b) => a != 0 && b != 0 ? 1 : 0, false, false),
		new binaryop("|", (a, b) => a | b, false, false),
		new binaryop("^", (a, b) => a ^ b, false, false),
		new binaryop("&", (a, b) => a & b, false, false),
	];

	public static readonly compareop[] relevant_compare_ops =
	[
		new compareop("!=", (a, b) => a != b, (a, b) => a != b),
		new compareop("==", (a, b) => a == b, (a, b) => a == b),
		new compareop("=~", (a, b) => a == b, (a, b) => glob.is_match(b, a)),
		new compareop(">=", (a, b) => a >= b, (a, b) => compare_text(a, b) >= 0),
		new compareop(">", (a, b) => a > b, (a, b) => compare_text(a, b) > 0),
		new compareop("<=", (a, b) => a <= b, (a, b) => compare_text(a, b) <= 0),
		new compareop("<", (a, b) => a < b, (a, b) => compare_text(a, b) < 0),
	];

	public static readonly binaryop[] relevant_arithmetic_ops =
	[
		new binaryop("+", (a, b) => unchecked(a + b), false, false),
		new binaryop("-", (a, b) => unchecked(a - b), true, false),
		new binaryop("*", (a, b) => unchecked(a * b), false, false),
		new binaryop("/", (a, b) => (int)unchecked((long)a / b), false, true),
		new binaryop("%", (a, b) => (int)unchecked((long)a % b), false, true),
	];

	public static readonly Dictionary<char, Func<int, int>> relevant_unary_ops = new()
	{
		['~'] = a => ~a,
		['!'] = a => a == 0 ? 1 : 0,
		['-'] = a => unchecked(-a),
	};
}

internal readonly record struct binaryop(string sep, Func<int, int, int> apply, bool needs_left, bool checks_zero);

internal readonly record struct compareop(string sep, Func<int, int, bool> numeric, Func<string, string, bool> text);

internal static class arithmetic
{
	public static result<int> evaluate(string expr)
	{
		return evaluate_range(expr, 0, expr.Length);
	}

	public static string to_text(int value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}

	private static result<int> evaluate_range(string buf, int pos1, int pos2)
	{
		while (pos1 < pos2 && syntax.is_white(buf[pos1]))
		{
			pos1++;
		}

		while (pos1 < pos2 && syntax.is_white(buf[pos2 - 1]))
		{
			pos2--;
		}

		if (pos1 == pos2)
		{
			return result<int>.fail("");
		}

		foreach (binaryop op in priv.relevant_logic_ops)
		{
			if (get_split(buf, pos1, pos2, op.sep, out int split))
			{
				return evaluate_binary(buf, pos1, pos2, split, op);
			}
		}

		foreach (compareop op in priv.relevant_compare_ops)
		{
			if (get_split(buf, pos1, pos2, op.sep, out int split))
			{
				return evaluate_compare(buf, pos1, pos2, split, op);
			}
		}

		foreach (binaryop op in priv.relevant_arithmetic_ops)
		{
			if (get_split(buf, pos1, pos2, op.sep, out int split) && (!op.needs_left || split != pos1))
			{
				return evaluate_binary(buf, pos1, pos2, split, op);
			}
		}

		if (priv.relevant_unary_ops.TryGetValue(buf[pos1], out Func<int, int>? unary))
		{
			result<int> operand = evaluate_range(buf, pos1 + 1, pos2);

			return operand.state ? result<int>.ok(unary(operand.value)) : operand;
		}

		if (string.CompareOrdinal(buf, pos1, priv.length_call, 0, priv.length_call.Length) == 0)
		{
			return buf[pos2 - 1] == ')' ? result<int>.ok(pos2 - pos1 - priv.length_empty.Length) : result<int>.fail("");
		}

		if (buf[pos1] == '(')
		{
			return buf[pos2 - 1] == ')' ? evaluate_range(buf, pos1 + 1, pos2 - 1) : result<int>.fail("");
		}

		return read_number(buf, pos1, pos2);
	}

	private static result<int> evaluate_binary(string buf, int pos1, int pos2, int split, binaryop op)
	{
		result<int> left = evaluate_range(buf, pos1, split);
		result<int> right = evaluate_range(buf, split + op.sep.Length, pos2);

		if (!left.state || !right.state)
		{
			return result<int>.fail("");
		}

		if (op.checks_zero && right.value == 0)
		{
			throw new gpperror("Division by zero in expression");
		}

		return result<int>.ok(op.apply(left.value, right.value));
	}

	private static result<int> evaluate_compare(string buf, int pos1, int pos2, int split, compareop op)
	{
		int right_start = split + op.sep.Length;
		result<int> left = evaluate_range(buf, pos1, split);
		result<int> right = evaluate_range(buf, right_start, pos2);

		if (left.state && right.state)
		{
			return result<int>.ok(op.numeric(left.value, right.value) ? 1 : 0);
		}

		int left_end = split;

		while (pos1 < left_end && syntax.is_white(buf[left_end - 1]))
		{
			left_end--;
		}

		while (pos2 > right_start && syntax.is_white(buf[right_start]))
		{
			right_start++;
		}

		return result<int>.ok(op.text(buf[pos1..left_end], buf[right_start..pos2]) ? 1 : 0);
	}

	private static bool get_split(string buf, int pos1, int pos2, string sep, out int split)
	{
		int depth = 0;

		for (int pos = pos2 - 1; pos >= pos1; pos--)
		{
			char c = buf[pos];

			if (c == ')')
			{
				depth++;
			}

			if (c == '(')
			{
				depth--;
			}

			if (depth < 0)
			{
				break;
			}

			if (depth == 0 && pos2 - pos >= sep.Length && string.CompareOrdinal(buf, pos, sep, 0, sep.Length) == 0)
			{
				split = pos;

				return true;
			}
		}

		split = 0;

		return false;
	}

	private static result<int> read_number(string s, int pos1, int pos2)
	{
		int i = pos1;
		bool negative = false;

		if (i < pos2 && (s[i] == '+' || s[i] == '-'))
		{
			negative = s[i] == '-';
			i++;
		}

		int number_base = priv.decimal_base;

		if (i + 2 < pos2 && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X') && Uri.IsHexDigit(s[i + 2]))
		{
			number_base = priv.hex_base;
			i += 2;
		}
		else if (i < pos2 && s[i] == '0')
		{
			number_base = priv.octal_base;
		}

		int digits_start = i;
		long limit = (long)int.MaxValue + 1;
		long value = 0;

		while (i < pos2)
		{
			int digit = get_digit(s[i]);

			if (digit >= number_base)
			{
				break;
			}

			value = Math.Min(value * number_base + digit, limit);
			i++;
		}

		if (i == digits_start || i != pos2)
		{
			return result<int>.fail("");
		}

		long signed = negative ? -value : value;

		return result<int>.ok((int)Math.Clamp(signed, int.MinValue, int.MaxValue));
	}

	private static int get_digit(char c)
	{
		if (c is >= '0' and <= '9')
		{
			return c - '0';
		}

		if (c is >= 'a' and <= 'z')
		{
			return c - 'a' + priv.decimal_base;
		}

		if (c is >= 'A' and <= 'Z')
		{
			return c - 'A' + priv.decimal_base;
		}

		return int.MaxValue;
	}
}

file static partial class priv
{
	public static int compare_text(string a, string b)
	{
		int common = Math.Min(a.Length, b.Length);
		int order = string.CompareOrdinal(a, 0, b, 0, common);

		if (order != 0)
		{
			return order;
		}

		return a.Length.CompareTo(b.Length);
	}
}
