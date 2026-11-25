namespace lmake.tests;

public sealed class utiltests
{
	[Theory]
	[InlineData("1 + 2 * 3", "7")]
	[InlineData("(1 + 2) * 3", "9")]
	[InlineData("10 - 4 - 3", "3")]
	[InlineData("0x10 + 010", "24")]
	[InlineData("7 / 2", "3")]
	[InlineData("-7 % 3", "-1")]
	[InlineData("!0 && ~0", "1")]
	[InlineData("1 || 0 && 0", "1")]
	[InlineData("6 & 3 | 8 ^ 1", "11")]
	[InlineData("abc == abc", "1")]
	[InlineData(" abc  != abd ", "1")]
	[InlineData("b > a", "1")]
	[InlineData("ab <= a", "0")]
	[InlineData("length(hello)", "5")]
	[InlineData("file.luau =~ *.lu?u", "1")]
	[InlineData("2147483647 + 1", "-2147483648")]
	public void is_expression_evaluated(string expr, string expected)
	{
		result<int> value = arithmetic.evaluate(expr);

		Assert.True(value.state);
		Assert.Equal(expected, arithmetic.to_text(value.value));
	}

	[Theory]
	[InlineData("")]
	[InlineData("hello")]
	[InlineData("1 +")]
	[InlineData("08")]
	[InlineData("(1")]
	public void is_non_numeric_expression_rejected(string expr)
	{
		Assert.False(arithmetic.evaluate(expr).state);
	}

	[Fact]
	public void is_division_by_zero_an_error()
	{
		Assert.Throws<gpperror>(() => arithmetic.evaluate("1 / 0"));
	}

	[Theory]
	[InlineData("*.luau", "a/b.luau", true)]
	[InlineData("a?c", "abc", true)]
	[InlineData("[a-c]x", "bx", true)]
	[InlineData("[!a-c]x", "bx", false)]
	[InlineData("\\*", "*", true)]
	[InlineData("[", "[", true)]
	[InlineData("a*", "b", false)]
	public void is_fnmatch_glob_matched(string pattern, string text, bool expected)
	{
		Assert.Equal(expected, glob.is_match(pattern, text));
	}

	[Theory]
	[InlineData("**/*.spec.luau", "a/b/c.spec.luau", true)]
	[InlineData("**/*.spec.luau", "c.spec.luau", true)]
	[InlineData("*.spec.luau", "deep/c.spec.luau", true)]
	[InlineData("tests/*", "tests/a/b.luau", false)]
	[InlineData("tests/**", "tests/a/b.luau", true)]
	[InlineData("vendor", "vendor/x.luau", true)]
	[InlineData("src/*.luau", "src/sub/x.luau", false)]
	public void is_path_glob_matched(string pattern, string path, bool expected)
	{
		Assert.Equal(expected, glob.is_path_match(pattern, path));
	}

	[Fact]
	public void is_date_formatted()
	{
		DateTime time = new DateTime(2026, 3, 7, 14, 5, 9);

		Assert.Equal("2026-03-07 14:05:09 Sat Mar 02 PM %", strftime.to_text("%F %T %a %b %I %p %%", time));
	}

	[Theory]
	[InlineData("\\b", true)]
	[InlineData(" ", true)]
	[InlineData("\\!w", false)]
	[InlineData("a", false)]
	public void is_command_line_syntax_encoded(string spec, bool expected_valid)
	{
		Assert.Equal(expected_valid, syntax.from_command_line(spec).state);
	}
}
