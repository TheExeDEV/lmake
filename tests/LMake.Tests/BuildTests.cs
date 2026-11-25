namespace lmake.tests;

public sealed class buildtests
{
	private static IEnumerable<string> get_relative_files(string root)
	{
		return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
			.Order(StringComparer.Ordinal);
	}

	[Fact]
	public void is_project_built_like_expected_tree()
	{
		using temptree tree = new temptree();
		tree.add_copy(fixtures.get_path("build", "basic"));
		result<settingsinfo> settings = settingsinfo.from_file(tree.get_path(project.config_file_name));
		Assert.True(settings.state, settings.message);
		builder build = builder.make(settings.value);
		result<buildinfo> first = build.run();

		Assert.True(first.state, first.message);
		Assert.Equal(4, first.value.processed);
		Assert.Equal(1, first.value.copied);
		Assert.Single(first.value.warnings);

		string expected_root = tree.get_path("expected");
		string output_root = tree.get_path("out");
		Assert.Equal(get_relative_files(expected_root), get_relative_files(output_root));

		foreach (string relative in get_relative_files(expected_root))
		{
			Assert.Equal(File.ReadAllText(Path.Combine(expected_root, relative)), File.ReadAllText(Path.Combine(output_root, relative)));
		}

		result<buildinfo> second = build.run();

		Assert.True(second.state, second.message);
		Assert.Equal(0, second.value.processed);
		Assert.Equal(5, second.value.unchanged);
	}

	[Fact]
	public void is_preprocess_error_reported_with_file()
	{
		using temptree tree = new temptree();
		tree.add_file("src/bad.luau", "--#endif\n");
		tree.add_file("src/good.luau", "return 1\n");
		result<buildinfo> built = builder.make(settingsinfo.make_default(tree.root)).run();

		Assert.False(built.state);
		Assert.Contains("src/bad.luau:1: error: #endif without #if", built.message);
		Assert.True(File.Exists(tree.get_path("out/good.luau")));
	}

	[Fact]
	public void is_line_ending_setting_applied()
	{
		using temptree tree = new temptree();
		tree.add_file("src/crlf.luau", "a\r\nb\r\n");
		tree.add_file("src/lf.luau", "a\nb\n");
		settingsinfo settings = settingsinfo.make_default(tree.root);

		Assert.True(builder.make(settings).run().state);
		Assert.Equal("a\r\nb\r\n", tree.read_file("out/crlf.luau"));
		Assert.Equal("a\nb\n", tree.read_file("out/lf.luau"));

		Assert.True(builder.make(settings with { line_endings = enumitem_line_endings.Lf, output = "lf" }).run().state);
		Assert.Equal("a\nb\n", tree.read_file("lf/crlf.luau"));
	}

	[Fact]
	public void is_output_inside_input_skipped()
	{
		using temptree tree = new temptree();
		tree.add_file("main.luau", "return 1\n");
		settingsinfo settings = settingsinfo.make_default(tree.root) with { input = ".", output = "out" };

		Assert.True(builder.make(settings).run().state);
		result<buildinfo> again = builder.make(settings).run();

		Assert.True(again.state, again.message);
		Assert.False(File.Exists(tree.get_path("out/out/main.luau")));
	}

	[Fact]
	public void is_single_file_built()
	{
		using temptree tree = new temptree();
		tree.add_file(".luaurc", "{ \"aliases\": { \"lib\": \"./lib\" } }");
		tree.add_file("lib/x.luau", "");
		string source = tree.add_file("app/main.luau", "--#define N 3\nreturn require(\"@lib/x\"), N\n");
		settingsinfo settings = settingsinfo.make_default(tree.root);
		result<rewriteinfo> built = builder.make(settings).run_file(source, tree.get_path("dist/main.luau"));

		Assert.True(built.state, built.message);
		Assert.Equal("return require(\"../lib/x\"), 3\n", built.value.text);
	}

	[Fact]
	public void is_preprocess_and_requires_optional()
	{
		using temptree tree = new temptree();
		tree.add_file(".luaurc", "{ \"aliases\": { \"lib\": \"./lib\" } }");
		string source = tree.add_file("main.luau", "--#define N 3\nreturn require(\"@lib/x\"), N\n");
		settingsinfo settings = settingsinfo.make_default(tree.root);
		settingsinfo disabled = settings with
		{
			preprocess = settings.preprocess with { enabled = false },
			requires = new requireinfo(false),
		};
		result<rewriteinfo> built = builder.make(disabled).run_file(source, source);

		Assert.True(built.state, built.message);
		Assert.Equal("--#define N 3\nreturn require(\"@lib/x\"), N\n", built.value.text);
	}

	[Fact]
	public void is_settings_file_parsed()
	{
		using temptree tree = new temptree();
		string path = tree.add_file(project.config_file_name, """
			{
			  "input": "source",
			  "output": "build",
			  "lineEndings": "crlf",
			  "preprocess": { "defines": { "A": "1", "B": true, "C": null, "D": 4 }, "allowExec": true },
			  "requires": { "enabled": false },
			}
			""");
		result<settingsinfo> settings = settingsinfo.from_file(path);

		Assert.True(settings.state, settings.message);
		Assert.Equal("source", settings.value.input);
		Assert.Equal("build", settings.value.output);
		Assert.Equal(enumitem_line_endings.Crlf, settings.value.line_endings);
		Assert.Equal(["A=1", "B=1", "C", "D=4"], settings.value.preprocess.defines);
		Assert.True(settings.value.preprocess.allow_exec);
		Assert.False(settings.value.requires.enabled);
	}

	[Theory]
	[InlineData("{ \"inptu\": \"src\" }", "unknown setting 'inptu'")]
	[InlineData("{ \"lineEndings\": \"cr\" }", "unknown line endings")]
	[InlineData("{ \"exclude\": [1] }", "must be an array of strings")]
	[InlineData("{ \"preprocess\": { \"defines\": { \"A\": [] } } }", "must be a string, number, true or null")]
	[InlineData("[]", "the root must be an object")]
	[InlineData("{", "")]
	public void is_invalid_settings_rejected(string json, string message)
	{
		using temptree tree = new temptree();
		result<settingsinfo> settings = settingsinfo.from_file(tree.add_file(project.config_file_name, json));

		Assert.False(settings.state);
		Assert.Contains(message, settings.message);
	}

	[Fact]
	public void is_default_settings_round_tripped()
	{
		using temptree tree = new temptree();
		settingsinfo defaults = settingsinfo.make_default(tree.root);
		string path = tree.add_file(project.config_file_name, defaults.to_json());
		result<settingsinfo> loaded = settingsinfo.from_file(path);

		Assert.True(loaded.state, loaded.message);
		Assert.Equal(defaults.to_json(), loaded.value.to_json());
	}
}
