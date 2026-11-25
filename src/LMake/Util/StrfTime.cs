using System.Globalization;
using System.Text;

namespace lmake;

file static partial class priv
{
	public const int abbreviation_length = 3;
	public const int century = 100;
	public const int noon = 12;

	public static readonly CultureInfo invariant = CultureInfo.InvariantCulture;

	public static readonly string[] day_names = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

	public static readonly string[] month_names =
	[
		"January", "February", "March", "April", "May", "June",
		"July", "August", "September", "October", "November", "December",
	];

	public static readonly Dictionary<char, Func<DateTime, string>> relevant_specifiers = new()
	{
		['a'] = t => day_names[(int)t.DayOfWeek][..abbreviation_length],
		['A'] = t => day_names[(int)t.DayOfWeek],
		['b'] = t => month_names[t.Month - 1][..abbreviation_length],
		['h'] = t => month_names[t.Month - 1][..abbreviation_length],
		['B'] = t => month_names[t.Month - 1],
		['c'] = t => strftime.to_text("%a %b %e %H:%M:%S %Y", t),
		['C'] = t => (t.Year / century).ToString("00", invariant),
		['d'] = t => t.Day.ToString("00", invariant),
		['D'] = t => strftime.to_text("%m/%d/%y", t),
		['e'] = t => t.Day.ToString(invariant).PadLeft(2),
		['F'] = t => strftime.to_text("%Y-%m-%d", t),
		['g'] = t => (ISOWeek.GetYear(t) % century).ToString("00", invariant),
		['G'] = t => ISOWeek.GetYear(t).ToString(invariant),
		['H'] = t => t.Hour.ToString("00", invariant),
		['I'] = t => get_hour12(t).ToString("00", invariant),
		['j'] = t => t.DayOfYear.ToString("000", invariant),
		['k'] = t => t.Hour.ToString(invariant).PadLeft(2),
		['l'] = t => get_hour12(t).ToString(invariant).PadLeft(2),
		['m'] = t => t.Month.ToString("00", invariant),
		['M'] = t => t.Minute.ToString("00", invariant),
		['n'] = t => "\n",
		['p'] = t => t.Hour < noon ? "AM" : "PM",
		['P'] = t => t.Hour < noon ? "am" : "pm",
		['r'] = t => strftime.to_text("%I:%M:%S %p", t),
		['R'] = t => strftime.to_text("%H:%M", t),
		['s'] = t => new DateTimeOffset(t).ToUnixTimeSeconds().ToString(invariant),
		['S'] = t => t.Second.ToString("00", invariant),
		['t'] = t => "\t",
		['T'] = t => strftime.to_text("%H:%M:%S", t),
		['u'] = t => (t.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)t.DayOfWeek).ToString(invariant),
		['U'] = t => ((t.DayOfYear + 6 - (int)t.DayOfWeek) / 7).ToString("00", invariant),
		['V'] = t => ISOWeek.GetWeekOfYear(t).ToString("00", invariant),
		['w'] = t => ((int)t.DayOfWeek).ToString(invariant),
		['W'] = t => ((t.DayOfYear + 6 - ((int)t.DayOfWeek + 6) % 7) / 7).ToString("00", invariant),
		['x'] = t => strftime.to_text("%m/%d/%y", t),
		['X'] = t => strftime.to_text("%H:%M:%S", t),
		['y'] = t => (t.Year % century).ToString("00", invariant),
		['Y'] = t => t.Year.ToString(invariant),
		['z'] = get_utc_offset,
		['Z'] = t => TimeZoneInfo.Local.IsDaylightSavingTime(t) ? TimeZoneInfo.Local.DaylightName : TimeZoneInfo.Local.StandardName,
		['%'] = t => "%",
	};

	public static readonly HashSet<char> relevant_locale_modifiers = ['E', 'O'];
}

internal static class strftime
{
	public static string to_text(string format, DateTime time)
	{
		StringBuilder text = new StringBuilder();

		for (int i = 0; i < format.Length; i++)
		{
			char c = format[i];

			if (c != '%' || i + 1 >= format.Length)
			{
				text.Append(c);
				continue;
			}

			i++;
			char key = format[i];

			if (priv.relevant_locale_modifiers.Contains(key) && i + 1 < format.Length)
			{
				i++;
				key = format[i];
			}

			if (priv.relevant_specifiers.TryGetValue(key, out Func<DateTime, string>? specifier))
			{
				text.Append(specifier(time));
			}
			else
			{
				text.Append('%').Append(key);
			}
		}

		return text.ToString();
	}
}

file static partial class priv
{
	public static int get_hour12(DateTime t)
	{
		int hour = t.Hour % noon;

		return hour == 0 ? noon : hour;
	}

	public static string get_utc_offset(DateTime t)
	{
		TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(t);
		TimeSpan magnitude = offset.Duration();
		string sign = offset < TimeSpan.Zero ? "-" : "+";

		return sign + magnitude.Hours.ToString("00", invariant) + magnitude.Minutes.ToString("00", invariant);
	}
}
