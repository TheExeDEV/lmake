namespace lmake.tests;

public sealed class luautests
{
	[Fact]
	public void is_code_tokenized()
	{
		string text = "local x = 1.5e-3\nlocal y = a..b :: any";
		IReadOnlyList<tokeninfo> tokens = luaulexer.read_tokens(text);

		Assert.Equal(["local", "x", "=", "1.5e-3", "local", "y", "=", "a", "..", "b", "::", "any"], tokens.Select(t => t.value));
		Assert.Equal(enumitem_token.Number, tokens[3].kind);
	}

	[Fact]
	public void is_interpolation_code_tokenized()
	{
		IReadOnlyList<tokeninfo> tokens = luaulexer.read_tokens("print(`a {require(\"x\")} b {{}} c`)");

		Assert.Contains(tokens, t => t.kind == enumitem_token.Name && t.value == "require");
		Assert.Contains(tokens, t => t.kind == enumitem_token.String && t.value == "\"x\"");
		Assert.Equal(")", tokens[^1].value);
	}

	[Theory]
	[InlineData("\"a\\tb\"", "a\tb")]
	[InlineData("'it\\'s'", "it's")]
	[InlineData("\"\\65\\x42\\u{43}\"", "ABC")]
	[InlineData("\"a\\z   \n  b\"", "ab")]
	[InlineData("[[\nline]]", "line")]
	[InlineData("[=[a]]b]=]", "a]]b")]
	public void is_string_value_decoded(string literal, string expected)
	{
		IReadOnlyList<tokeninfo> tokens = luaulexer.read_tokens(literal);
		result<string> value = luaulexer.read_string_value(literal, tokens[0]);

		Assert.True(value.state, value.message);
		Assert.Equal(expected, value.value);
	}

	[Fact]
	public void is_alias_resolved_case_insensitively_and_inherited()
	{
		using temptree tree = new temptree();
		tree.add_file(".luaurc", "{ \"aliases\": { \"Pkg\": \"./packages\", \"lib\": \"./lib\" } }");
		tree.add_file("game/.luaurc", "{ \"aliases\": { \"lib\": \"../shared\", } }");
		luaurcresolver resolver = luaurcresolver.make();

		Assert.Equal(tree.get_path("packages"), resolver.get_alias_path(tree.get_path("game/src"), "pkg").value);
		Assert.Equal(tree.get_path("shared"), resolver.get_alias_path(tree.get_path("game"), "LIB").value);
		Assert.Equal(tree.get_path("lib"), resolver.get_alias_path(tree.root, "lib").value);
	}

	[Fact]
	public void is_alias_chain_resolved_and_cycle_rejected()
	{
		using temptree tree = new temptree();
		tree.add_file(".luaurc", "{ \"aliases\": { \"base\": \"./src\", \"util\": \"@base/util\", \"a\": \"@b\", \"b\": \"@a\" } }");
		luaurcresolver resolver = luaurcresolver.make();

		Assert.Equal(tree.get_path("src/util"), resolver.get_alias_path(tree.root, "util").value);
		Assert.Contains("refers to itself", resolver.get_alias_path(tree.root, "a").message);
		Assert.Contains("unknown alias", resolver.get_alias_path(tree.root, "missing").message);
		Assert.Contains("invalid alias", resolver.get_alias_path(tree.root, "bad name").message);
	}

	[Fact]
	public void is_broken_luaurc_reported()
	{
		using temptree tree = new temptree();
		tree.add_file(".luaurc", "{ \"aliases\": ");

		Assert.False(luaurcresolver.make().get_alias_path(tree.root, "x").state);
	}

	private static rewriteinfo get_rewritten(temptree tree, string source, string text)
	{
		requirerewriter rewriter = requirerewriter.make(luaurcresolver.make());
		pathmapinfo map = new pathmapinfo(tree.root, tree.root);
		result<rewriteinfo> rewritten = rewriter.apply(text, tree.get_path(source), map);
		Assert.True(rewritten.state, rewritten.message);

		return rewritten.value;
	}

	[Fact]
	public void is_require_rewritten_with_init_semantics()
	{
		using temptree tree = new temptree();
		tree.add_file(".luaurc", "{ \"aliases\": { \"shared\": \"./shared\" } }");
		tree.add_file("shared/util.luau", "return 1");
		tree.add_file("shared/init.luau", "return 1");
		tree.add_file("client/init.luau", "");
		tree.add_file("client/view.luau", "");

		Assert.Equal("require(\"../shared/util\")", get_rewritten(tree, "client/view.luau", "require(\"@shared/util\")").text);
		Assert.Equal("require(\"./shared/util\")", get_rewritten(tree, "client/init.luau", "require(\"@shared/util\")").text);
		Assert.Equal("require \"@self/util\"", get_rewritten(tree, "shared/init.luau", "require \"@shared/util\"").text);
		Assert.Equal("require('./util')", get_rewritten(tree, "shared/other.luau", "require('@shared/util')").text);
		Assert.Equal("require(\"../shared\")", get_rewritten(tree, "client/view.luau", "require(\"@shared\")").text);
		Assert.Equal("require(\"../shared\")", get_rewritten(tree, "shared/other.luau", "require(\"@shared\")").text);
	}

	[Fact]
	public void is_unrelated_code_left_alone()
	{
		using temptree tree = new temptree();
		tree.add_file(".luaurc", "{ \"aliases\": { \"shared\": \"./shared\" } }");
		tree.add_file("shared/util.luau", "");
		string text = "obj.require(\"@shared/util\") obj:require(\"@shared/util\") require(\"./x\") require(\"@self/y\") require(name)";
		rewriteinfo rewritten = get_rewritten(tree, "main.luau", text);

		Assert.Equal(text, rewritten.text);
		Assert.Equal(0, rewritten.changed);
		Assert.Empty(rewritten.warnings);
	}

	[Fact]
	public void is_problem_reported_as_warning()
	{
		using temptree tree = new temptree();
		tree.add_file(".luaurc", "{ \"aliases\": { \"shared\": \"./shared\" } }");
		tree.add_file("shared/both.luau", "");
		tree.add_file("shared/both.lua", "");
		string text = "require(\"@nope/x\")\nrequire(\"@shared/none\")\nrequire(\"@shared/both\")";
		rewriteinfo rewritten = get_rewritten(tree, "main.luau", text);

		Assert.Equal(2, rewritten.changed);
		Assert.Equal(3, rewritten.warnings.Count);
		Assert.Contains("main.luau:1: unknown alias '@nope'", rewritten.warnings[0]);
		Assert.Contains("does not resolve to a module", rewritten.warnings[1]);
		Assert.Contains("is ambiguous", rewritten.warnings[2]);
	}

	[Fact]
	public void is_output_mapping_applied()
	{
		using temptree tree = new temptree();
		tree.add_file(".luaurc", "{ \"aliases\": { \"vendor\": \"./vendor\", \"src\": \"./src\" } }");
		tree.add_file("vendor/lib.luau", "");
		tree.add_file("src/a.luau", "");
		requirerewriter rewriter = requirerewriter.make(luaurcresolver.make());
		pathmapinfo map = new pathmapinfo(tree.get_path("src"), tree.get_path("build/out"));
		string text = "require(\"@vendor/lib\") require(\"@src/a\")";
		result<rewriteinfo> rewritten = rewriter.apply(text, tree.get_path("src/main.luau"), map);

		Assert.True(rewritten.state, rewritten.message);
		Assert.Equal("require(\"../../vendor/lib\") require(\"./a\")", rewritten.value.text);
	}
}
