using System.Text.Json;

namespace lmake;

file static partial class priv
{
	public const string aliases_key = "aliases";
	public const char alias_prefix = '@';
	public const string alias_charset = "abcdefghijklmnopqrstuvwxyz0123456789.-_";

	public static readonly JsonDocumentOptions json_options = new JsonDocumentOptions
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip,
	};
}

internal readonly record struct aliasentry(string value, string config_directory);

public sealed partial class luaurcresolver
{
	private readonly Dictionary<string, result<Dictionary<string, aliasentry>?>> configs =
		new Dictionary<string, result<Dictionary<string, aliasentry>?>>(StringComparer.Ordinal);

	private luaurcresolver()
	{
	}

	public static partial luaurcresolver make()
	{
		return new luaurcresolver();
	}

	public partial result<string> get_alias_path(string requirer_directory, string alias)
	{
		return get_alias_path_from(Path.GetFullPath(requirer_directory), alias, []);
	}

	private result<string> get_alias_path_from(string directory, string alias, HashSet<string> visited)
	{
		string key = alias.ToLowerInvariant();

		if (key.Length == 0 || !key.All(priv.alias_charset.Contains))
		{
			return result<string>.fail($"invalid alias name '@{alias}'");
		}

		if (!visited.Add(key))
		{
			return result<string>.fail($"alias '@{alias}' refers to itself");
		}

		for (string? current = directory; current is not null; current = Path.GetDirectoryName(current))
		{
			result<Dictionary<string, aliasentry>?> config = read_config(current);

			if (!config.state)
			{
				return result<string>.fail(config.message);
			}

			if (config.value is null || !config.value.TryGetValue(key, out aliasentry entry))
			{
				continue;
			}

			if (entry.value.Length > 0 && entry.value[0] == priv.alias_prefix)
			{
				return get_chained_path(entry, visited);
			}

			return result<string>.ok(Path.GetFullPath(Path.Combine(entry.config_directory, entry.value)));
		}

		return result<string>.fail($"unknown alias '@{alias}' (no {project.luaurc_file_name} defines it)");
	}

	private result<string> get_chained_path(aliasentry entry, HashSet<string> visited)
	{
		string chained = entry.value[1..];
		int slash = chained.IndexOfAny(['/', '\\']);
		string next_alias = slash < 0 ? chained : chained[..slash];
		string rest = slash < 0 ? "" : chained[(slash + 1)..];
		result<string> base_path = get_alias_path_from(entry.config_directory, next_alias, visited);

		if (!base_path.state || rest.Length == 0)
		{
			return base_path;
		}

		return result<string>.ok(Path.GetFullPath(Path.Combine(base_path.value, rest)));
	}

	private result<Dictionary<string, aliasentry>?> read_config(string directory)
	{
		if (configs.TryGetValue(directory, out result<Dictionary<string, aliasentry>?> cached))
		{
			return cached;
		}

		result<Dictionary<string, aliasentry>?> loaded = read_config_file(directory);
		configs[directory] = loaded;

		return loaded;
	}

	private static result<Dictionary<string, aliasentry>?> read_config_file(string directory)
	{
		string path = Path.Combine(directory, project.luaurc_file_name);

		if (!File.Exists(path))
		{
			return result<Dictionary<string, aliasentry>?>.ok(null);
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), priv.json_options);
			Dictionary<string, aliasentry> aliases = new Dictionary<string, aliasentry>(StringComparer.Ordinal);
			bool has_aliases = document.RootElement.ValueKind == JsonValueKind.Object
				&& document.RootElement.TryGetProperty(priv.aliases_key, out JsonElement element)
				&& element.ValueKind == JsonValueKind.Object;

			if (!has_aliases)
			{
				return result<Dictionary<string, aliasentry>?>.ok(aliases);
			}

			foreach (JsonProperty property in document.RootElement.GetProperty(priv.aliases_key).EnumerateObject())
			{
				if (property.Value.ValueKind == JsonValueKind.String)
				{
					aliases[property.Name.ToLowerInvariant()] = new aliasentry(property.Value.GetString()!, directory);
				}
			}

			return result<Dictionary<string, aliasentry>?>.ok(aliases);
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			return result<Dictionary<string, aliasentry>?>.fail($"{path}: {exception.Message}");
		}
	}
}
