namespace lmake.tests;

public sealed class gppoptionstests
{
	[Fact]
	public void is_luau_syntax_parsed()
	{
		result<gppoptions> options = gppoptions.from_args(gppoptions.luau_syntax);

		Assert.True(options.state, options.message);
		Assert.Null(options.value.input_file);
	}

	[Fact]
	public void is_full_command_line_parsed()
	{
		string[] args = ["-o", "out.luau", "-Iinc", "-I", "more", "-DA=1", "-D", "B", "-x", "-z", "--include", "pre.luau", "main.luau"];
		result<gppoptions> options = gppoptions.from_args(args);

		Assert.True(options.state, options.message);
		Assert.Equal("out.luau", options.value.output_file);
		Assert.Equal(["inc", "more"], options.value.include_dirs);
		Assert.Equal(["A=1", "B"], options.value.defines);
		Assert.Equal(["pre.luau"], options.value.pre_includes);
		Assert.True(options.value.allow_exec);
		Assert.True(options.value.dos_line_endings);
		Assert.Equal("main.luau", options.value.input_file);
	}

	[Theory]
	[InlineData("-M", "--#", "\\n", " ", " ", "\\n", "(", ")")]
	[InlineData("-Q")]
	[InlineData("-C", "-T")]
	[InlineData("-o")]
	[InlineData("a.luau", "b.luau")]
	[InlineData("+cXYZ", "/*", "*/")]
	[InlineData("--includemarker", "%")]
	public void is_invalid_command_line_rejected(params string[] args)
	{
		Assert.False(gppoptions.from_args(args).state);
	}

	[Fact]
	public void is_end_of_options_respected()
	{
		result<gppoptions> options = gppoptions.from_args(["--", "-file.luau"]);

		Assert.True(options.state, options.message);
		Assert.Equal("-file.luau", options.value.input_file);
	}

	[Fact]
	public void is_help_and_version_detected()
	{
		Assert.True(gppoptions.from_args(["--help"]).value.show_help);
		Assert.True(gppoptions.from_args(["--version"]).value.show_version);
		Assert.Contains("-U : user-defined syntax", gppoptions.get_usage());
	}
}
