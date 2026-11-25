namespace lmake.tests;

public sealed class gppparitytests
{
	public static TheoryData<string> get_cases()
	{
		TheoryData<string> cases = [];

		foreach (string file in Directory.EnumerateFiles(fixtures.get_path("gpp"), "*.luau").Order(StringComparer.Ordinal))
		{
			cases.Add(Path.GetFileNameWithoutExtension(file));
		}

		return cases;
	}

	[Theory]
	[MemberData(nameof(get_cases))]
	public void is_output_identical_to_reference_gpp(string name)
	{
		string source = fixtures.get_path("gpp", name + ".luau");
		string expected = Path.ChangeExtension(source, ".expected");
		string error = Path.ChangeExtension(source, ".error");
		string text = File.ReadAllText(source);
		result<string> output = fixtures.make_luau_processor().process_text(text, name + ".luau", Path.GetDirectoryName(source)!);

		if (File.Exists(expected))
		{
			Assert.True(output.state, output.message);
			Assert.Equal(File.ReadAllText(expected), output.value);

			return;
		}

		Assert.True(File.Exists(error), $"{name} has neither .expected nor .error");
		Assert.False(output.state);
		Assert.EndsWith("error: " + File.ReadAllText(error).Trim(), output.message);
	}
}
