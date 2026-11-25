namespace lmake;

file static partial class priv
{
	public const string any_depth = "**";
}

internal static class glob
{
	public static bool is_match(string pattern, string text)
	{
		return is_match_at(pattern, 0, text, 0, false);
	}

	public static bool is_path_match(string pattern, string path)
	{
		string normalized_pattern = pattern.Replace('\\', '/').TrimStart('/');
		string normalized_path = path.Replace('\\', '/');

		if (!normalized_pattern.Contains('/'))
		{
			return normalized_path.Split('/').Any(part => is_match_at(normalized_pattern, 0, part, 0, true));
		}

		return is_segment_match(normalized_pattern.Split('/'), 0, normalized_path.Split('/'), 0);
	}

	private static bool is_segment_match(string[] pattern, int pi, string[] path, int ti)
	{
		if (pi == pattern.Length)
		{
			return ti == path.Length;
		}

		if (pattern[pi] == priv.any_depth)
		{
			for (int skip = ti; skip <= path.Length; skip++)
			{
				if (is_segment_match(pattern, pi + 1, path, skip))
				{
					return true;
				}
			}

			return false;
		}

		if (ti == path.Length || !is_match_at(pattern[pi], 0, path[ti], 0, true))
		{
			return false;
		}

		return is_segment_match(pattern, pi + 1, path, ti + 1);
	}

	private static bool is_match_at(string p, int pi, string t, int ti, bool stop_at_slash)
	{
		while (pi < p.Length)
		{
			char c = p[pi];

			if (c == '*')
			{
				while (pi < p.Length && p[pi] == '*')
				{
					pi++;
				}

				if (pi == p.Length)
				{
					return !stop_at_slash || !t[ti..].Contains('/');
				}

				for (int k = ti; k <= t.Length; k++)
				{
					if (is_match_at(p, pi, t, k, stop_at_slash))
					{
						return true;
					}

					if (stop_at_slash && k < t.Length && t[k] == '/')
					{
						return false;
					}
				}

				return false;
			}

			if (ti >= t.Length)
			{
				return false;
			}

			if (c == '?')
			{
				pi++;
				ti++;
				continue;
			}

			if (c == '[')
			{
				int end = get_bracket_end(p, pi);

				if (end >= 0)
				{
					if (!is_bracket_match(p, pi + 1, end, t[ti]))
					{
						return false;
					}

					pi = end + 1;
					ti++;
					continue;
				}
			}

			if (c == '\\' && pi + 1 < p.Length)
			{
				pi++;
				c = p[pi];
			}

			if (t[ti] != c)
			{
				return false;
			}

			pi++;
			ti++;
		}

		return ti == t.Length;
	}

	private static int get_bracket_end(string p, int open)
	{
		int i = open + 1;

		if (i < p.Length && (p[i] == '!' || p[i] == '^'))
		{
			i++;
		}

		if (i < p.Length && p[i] == ']')
		{
			i++;
		}

		while (i < p.Length && p[i] != ']')
		{
			if (p[i] == '\\' && i + 1 < p.Length)
			{
				i++;
			}

			i++;
		}

		return i < p.Length ? i : -1;
	}

	private static bool is_bracket_match(string p, int start, int end, char c)
	{
		bool negate = p[start] == '!' || p[start] == '^';
		int i = negate ? start + 1 : start;
		bool matched = false;

		while (i < end)
		{
			char low = p[i];

			if (low == '\\' && i + 1 < end)
			{
				i++;
				low = p[i];
			}

			if (i + 2 < end && p[i + 1] == '-')
			{
				char high = p[i + 2];
				matched |= c >= low && c <= high;
				i += 3;
				continue;
			}

			matched |= c == low;
			i++;
		}

		return matched != negate;
	}
}
