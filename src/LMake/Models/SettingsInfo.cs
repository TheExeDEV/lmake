using System.Text;
using System.Text.Json;

namespace lmake;

file static partial class priv
{
	public const string schema_key = "$schema";

	public delegate result<settingsinfo> settingreader(settingsinfo settings, JsonElement value);

	public delegate result<preprocessinfo> preprocessreader(preprocessinfo preprocess, JsonElement value);

	public static readonly JsonDocumentOptions json_options = new JsonDocumentOptions
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip,
	};

	public static readonly JsonWriterOptions writer_options = new JsonWriterOptions { Indented = true, IndentSize = 2 };

	public static readonly Dictionary<string, settingreader> relevant_settings = new(StringComparer.Ordinal)
	{
		[schema_key] = (s, v) => result<settingsinfo>.ok(s),
		["input"] = (s, v) => from_text(v, "input", text => s with { input = text }),
		["output"] = (s, v) => from_text(v, "output", text => s with { output = text }),
		["extensions"] = (s, v) => from_list(v, "extensions", list => s with { extensions = list }),
		["exclude"] = (s, v) => from_list(v, "exclude", list => s with { exclude = list }),
		["copyOtherFiles"] = (s, v) => from_bool(v, "copyOtherFiles", flag => s with { copy_other_files = flag }),
		["lineEndings"] = read_line_endings,
		["preprocess"] = read_preprocess,
		["requires"] = read_requires,
	};

	public static readonly Dictionary<string, preprocessreader> relevant_preprocess = new(StringComparer.Ordinal)
	{
		["enabled"] = (p, v) => from_bool(v, "preprocess.enabled", flag => p with { enabled = flag }),
		["syntax"] = (p, v) => v.ValueKind == JsonValueKind.Null
			? result<preprocessinfo>.ok(p with { syntax = gppoptions.luau_syntax })
			: from_list(v, "preprocess.syntax", list => p with { syntax = list }),
		["defines"] = read_defines,
		["includeDirs"] = (p, v) => from_list(v, "preprocess.includeDirs", list => p with { include_dirs = list }),
		["prelude"] = (p, v) => from_list(v, "preprocess.prelude", list => p with { prelude = list }),
		["allowExec"] = (p, v) => from_bool(v, "preprocess.allowExec", flag => p with { allow_exec = flag }),
	};
}

public readonly partial record struct settingsinfo
{
	public static partial settingsinfo make_default(string root)
	{
		preprocessinfo preprocess = new preprocessinfo(true, gppoptions.luau_syntax, [], [], [], false);

		return new settingsinfo(
			Path.GetFullPath(root),
			project.default_input,
			project.default_output,
			project.default_extensions,
			[],
			true,
			enumitem_line_endings.Auto,
			preprocess,
			new requireinfo(true));
	}

	public static partial result<settingsinfo> from_file(string path)
	{
		string root = Path.GetDirectoryName(Path.GetFullPath(path))!;
		settingsinfo settings = make_default(root);

		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), priv.json_options);

			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return result<settingsinfo>.fail($"{path}: the root must be an object");
			}

			foreach (JsonProperty property in document.RootElement.EnumerateObject())
			{
				if (!priv.relevant_settings.TryGetValue(property.Name, out priv.settingreader? reader))
				{
					return result<settingsinfo>.fail($"{path}: unknown setting '{property.Name}'");
				}

				result<settingsinfo> applied = reader(settings, property.Value);

				if (!applied.state)
				{
					return result<settingsinfo>.fail($"{path}: {applied.message}");
				}

				settings = applied.value;
			}

			return result<settingsinfo>.ok(settings);
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			return result<settingsinfo>.fail($"{path}: {exception.Message}");
		}
	}

	public partial string to_json()
	{
		using MemoryStream stream = new MemoryStream();

		using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, priv.writer_options))
		{
			writer.WriteStartObject();
			writer.WriteString("input", input);
			writer.WriteString("output", output);
			priv.add_list(writer, "extensions", extensions);
			priv.add_list(writer, "exclude", exclude);
			writer.WriteBoolean("copyOtherFiles", copy_other_files);
			writer.WriteString("lineEndings", enum_line_endings.to_text(line_endings));
			writer.WriteStartObject("preprocess");
			writer.WriteBoolean("enabled", preprocess.enabled);
			writer.WriteStartObject("defines");

			foreach (string define in preprocess.defines)
			{
				int equals = define.IndexOf('=');
				writer.WriteString(equals < 0 ? define : define[..equals], equals < 0 ? "" : define[(equals + 1)..]);
			}

			writer.WriteEndObject();
			priv.add_list(writer, "includeDirs", preprocess.include_dirs);
			priv.add_list(writer, "prelude", preprocess.prelude);
			writer.WriteBoolean("allowExec", preprocess.allow_exec);
			writer.WriteEndObject();
			writer.WriteStartObject("requires");
			writer.WriteBoolean("enabled", requires.enabled);
			writer.WriteEndObject();
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
	}
}

file static partial class priv
{
	public static result<T> from_text<T>(JsonElement value, string name, Func<string, T> apply)
	{
		if (value.ValueKind != JsonValueKind.String)
		{
			return result<T>.fail($"'{name}' must be a string");
		}

		return result<T>.ok(apply(value.GetString()!));
	}

	public static result<settingsinfo> fail_type(string name, string expected)
	{
		return result<settingsinfo>.fail($"'{name}' must be {expected}");
	}

	public static result<T> from_bool<T>(JsonElement value, string name, Func<bool, T> apply)
	{
		if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
		{
			return result<T>.fail($"'{name}' must be true or false");
		}

		return result<T>.ok(apply(value.GetBoolean()));
	}

	public static result<T> from_list<T>(JsonElement value, string name, Func<IReadOnlyList<string>, T> apply)
	{
		if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
		{
			return result<T>.fail($"'{name}' must be an array of strings");
		}

		List<string> list = [.. value.EnumerateArray().Select(item => item.GetString()!)];

		return result<T>.ok(apply(list));
	}

	public static result<settingsinfo> read_line_endings(settingsinfo settings, JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.String)
		{
			return fail_type("lineEndings", $"one of: {string.Join(", ", enum_line_endings.get_names())}");
		}

		result<enumitem_line_endings> parsed = enum_line_endings.from_text(value.GetString()!);

		if (!parsed.state)
		{
			return result<settingsinfo>.fail(parsed.message);
		}

		return result<settingsinfo>.ok(settings with { line_endings = parsed.value });
	}

	public static result<settingsinfo> read_preprocess(settingsinfo settings, JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			return fail_type("preprocess", "an object");
		}

		preprocessinfo preprocess = settings.preprocess;

		foreach (JsonProperty property in value.EnumerateObject())
		{
			if (!relevant_preprocess.TryGetValue(property.Name, out preprocessreader? reader))
			{
				return result<settingsinfo>.fail($"unknown setting 'preprocess.{property.Name}'");
			}

			result<preprocessinfo> applied = reader(preprocess, property.Value);

			if (!applied.state)
			{
				return result<settingsinfo>.fail(applied.message);
			}

			preprocess = applied.value;
		}

		return result<settingsinfo>.ok(settings with { preprocess = preprocess });
	}

	public static result<settingsinfo> read_requires(settingsinfo settings, JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			return fail_type("requires", "an object");
		}

		requireinfo requires = settings.requires;

		foreach (JsonProperty property in value.EnumerateObject())
		{
			if (property.Name != "enabled")
			{
				return result<settingsinfo>.fail($"unknown setting 'requires.{property.Name}'");
			}

			result<requireinfo> applied = from_bool(property.Value, "requires.enabled", flag => new requireinfo(flag));

			if (!applied.state)
			{
				return result<settingsinfo>.fail(applied.message);
			}

			requires = applied.value;
		}

		return result<settingsinfo>.ok(settings with { requires = requires });
	}

	public static result<preprocessinfo> read_defines(preprocessinfo preprocess, JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			return result<preprocessinfo>.fail("'preprocess.defines' must be an object");
		}

		List<string> defines = [];

		foreach (JsonProperty property in value.EnumerateObject())
		{
			string? text = property.Value.ValueKind switch
			{
				JsonValueKind.String => property.Value.GetString(),
				JsonValueKind.Number => property.Value.GetRawText(),
				JsonValueKind.True => "1",
				JsonValueKind.Null => "",
				_ => null,
			};

			if (text is null)
			{
				return result<preprocessinfo>.fail($"'preprocess.defines.{property.Name}' must be a string, number, true or null");
			}

			defines.Add(text.Length == 0 ? property.Name : $"{property.Name}={text}");
		}

		return result<preprocessinfo>.ok(preprocess with { defines = defines });
	}

	public static void add_list(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
	{
		writer.WriteStartArray(name);

		foreach (string value in values)
		{
			writer.WriteStringValue(value);
		}

		writer.WriteEndArray();
	}
}
