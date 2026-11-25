namespace lmake;

file static partial class priv
{
	public static readonly Dictionary<enumitem_line_endings, string> line_endings_names = new()
	{
		[enumitem_line_endings.Auto] = "auto",
		[enumitem_line_endings.Lf] = "lf",
		[enumitem_line_endings.Crlf] = "crlf",
	};
}

internal static class enum_line_endings
{
	public static string to_text(enumitem_line_endings value)
	{
		return priv.line_endings_names[value];
	}

	public static result<enumitem_line_endings> from_text(string text)
	{
		foreach ((enumitem_line_endings value, string name) in priv.line_endings_names)
		{
			if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
			{
				return result<enumitem_line_endings>.ok(value);
			}
		}

		string expected = string.Join(", ", priv.line_endings_names.Values);

		return result<enumitem_line_endings>.fail($"unknown line endings '{text}', expected one of: {expected}");
	}

	public static IReadOnlyCollection<string> get_names()
	{
		return priv.line_endings_names.Values;
	}
}
