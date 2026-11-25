namespace lmake.tests;

public sealed class gppprocessortests
{
	[Theory]
	[InlineData("--#define A 1\nA\n", "1\n")]
	[InlineData("--#define F(x, y) [x|y]\nF(1, (2, 3))\n", "[1| (2, 3)]\n")]
	[InlineData("--#define G #2#1\nG(a, b)\n", " ba\n")]
	[InlineData("local n = #list\n", "local n = #list\n")]
	[InlineData("x = --[[# 1 + 2 ]] --[[#--#eval 1 + 2\n]]\n", "x =  1 + 2  3\n")]
	[InlineData("--#ifdef MISSING\nno\n--#else\nyes\n--#endif\n", "yes\n")]
	public void is_luau_syntax_applied(string input, string expected)
	{
		result<string> output = fixtures.get_luau_output(input);

		Assert.True(output.state, output.message);
		Assert.Equal(expected, output.value);
	}

	[Fact]
	public void is_command_line_define_with_named_args_expanded()
	{
		result<string> output = fixtures.get_luau_output("PAIR(1, 2) FLAG\n", "-DPAIR(a,b)={a; b}", "-DFLAG");

		Assert.True(output.state, output.message);
		Assert.Equal("{1;  2} \n", output.value);
	}

	[Fact]
	public void is_error_reported_with_file_and_line()
	{
		result<string> output = fixtures.get_luau_output("a\nb\n--#error broken build\n");

		Assert.False(output.state);
		Assert.Equal("test.luau:3: error: broken build", output.message);
	}

	[Fact]
	public void is_warning_sent_to_handler()
	{
		gppprocessor processor = fixtures.make_luau_processor();
		List<string> warnings = [];
		processor.on_warning = warnings.Add;
		result<string> output = processor.process_text("--#warning careful\n--#exec echo hi\n", "w.luau", ".");

		Assert.True(output.state, output.message);
		Assert.Equal(2, warnings.Count);
		Assert.Equal("w.luau:1: warning: careful", warnings[0]);
		Assert.Contains("Not allowed to #exec", warnings[1]);
	}

	[Fact]
	public void is_exec_output_included_when_allowed()
	{
		result<string> output = fixtures.get_luau_output("--#exec echo hello\n", "-x");

		Assert.True(output.state, output.message);
		Assert.Equal("hello\n", output.value);
	}

	[Fact]
	public void is_pre_include_processed_first()
	{
		using temptree tree = new temptree();
		string prelude = tree.add_file("prelude.luau", "--#define FROM_PRELUDE yes\n");
		result<string> output = fixtures.get_luau_output("FROM_PRELUDE\n", "--include", prelude);

		Assert.True(output.state, output.message);
		Assert.Equal("yes\n", output.value);
	}

	[Fact]
	public void is_include_found_in_include_dirs()
	{
		using temptree tree = new temptree();
		tree.add_file("lib/shared.luau", "--#define SHARED 7\n");
		result<string> output = fixtures.get_luau_output("--#include shared.luau\nSHARED\n", "-I" + tree.get_path("lib"));

		Assert.True(output.state, output.message);
		Assert.Equal("7\n", output.value);
	}

	[Fact]
	public void is_missing_include_an_error()
	{
		result<string> output = fixtures.get_luau_output("--#include nowhere.luau\n");

		Assert.False(output.state);
		Assert.Contains("Requested include file not found", output.message);
	}

	[Fact]
	public void is_recursive_macro_stopped()
	{
		result<string> output = fixtures.get_luau_output("--#define LOOP LOOP\nLOOP\n");

		Assert.False(output.state);
		Assert.Contains("Stack depth exceeded", output.message);
	}

	[Fact]
	public void is_dos_line_ending_option_applied()
	{
		result<string> output = fixtures.get_luau_output("a\nb\n", "-z");

		Assert.True(output.state, output.message);
		Assert.Equal("a\r\nb\r\n", output.value);
	}

	[Fact]
	public void is_glob_comparison_supported()
	{
		result<string> output = fixtures.get_luau_output("--#if main.luau =~ *.luau\nyes\n--#endif\n");

		Assert.True(output.state, output.message);
		Assert.Equal("yes\n", output.value);
	}

	[Fact]
	public void is_default_mode_usable()
	{
		result<gppoptions> options = gppoptions.from_args([]);
		Assert.True(options.state, options.message);
		result<gppprocessor> processor = gppprocessor.make(options.value);
		Assert.True(processor.state, processor.message);
		result<string> output = processor.value.process_text("#define X(a) <a>\nX(1) \\X(2)\n", "c.txt", ".");

		Assert.True(output.state, output.message);
		Assert.Equal("<1> X(2)\n", output.value);
	}

	[Fact]
	public void is_defined_reported_after_processing()
	{
		gppprocessor processor = fixtures.make_luau_processor();
		result<string> output = processor.process_text("--#define KEEP 1\n", "d.luau", ".");

		Assert.True(output.state, output.message);
		Assert.True(processor.is_defined("KEEP"));
		Assert.False(processor.is_defined("defined"));
	}
}
