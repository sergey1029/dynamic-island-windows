using System.Globalization;

namespace DynamicIsland;

public sealed record CalendarEvent(string Uid, string Title, DateTime Start, DateTime End, bool AllDay, string Location)
{
    public string Color { get; init; } = "#FF453A";
}

/// <summary>Загружает события из iCal-ссылок (Google, Яндекс, Outlook, iCloud) или локальных .ics файлов.</summary>
public sealed class CalendarService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<List<CalendarEvent>> LoadAsync(IEnumerable<CalendarSource> sources, DateTime from, DateTime to)
    {
        var all = new List<CalendarEvent>();
        foreach (var source in sources.Where(s => s.Enabled))
        {
            try
            {
                var text = await FetchAsync(source.Url);
                all.AddRange(IcsParser.Parse(text, from, to).Select(e => e with { Color = source.Color }));
            }
            catch (Exception ex)
            {
                Log.Write("calendar " + source.Name, ex);
            }
        }
        return all.OrderBy(e => e.Start).ToList();
    }

    /// <summary>Проверка ссылки перед подключением: сколько событий в ближайший месяц.</summary>
    public async Task<(bool Ok, int Count, string Error)> TestAsync(string url)
    {
        try
        {
            var text = await FetchAsync(url);
            if (!text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
                return (false, 0, "По ссылке не календарь iCal. Проверьте, что скопирована ссылка формата .ics");
            var today = DateTime.Today;
            return (true, IcsParser.Parse(text, today, today.AddDays(30)).Count, "");
        }
        catch (HttpRequestException ex)
        {
            return (false, 0, ex.StatusCode is { } code ? $"Сервер ответил {(int)code}. Ссылка неверная или закрытая" : "Не удалось подключиться к серверу");
        }
        catch (TaskCanceledException)
        {
            return (false, 0, "Сервер не ответил вовремя");
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }

    static async Task<string> FetchAsync(string source)
    {
        source = source.Trim();
        if (File.Exists(source)) return await File.ReadAllTextAsync(source);
        var url = source.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + source[9..] : source;
        return await Http.GetStringAsync(url);
    }
}

/// <summary>Минимальный разбор iCalendar: VEVENT, часовые пояса, RRULE (DAILY/WEEKLY/MONTHLY/YEARLY), EXDATE.</summary>
static class IcsParser
{
    public static List<CalendarEvent> Parse(string text, DateTime from, DateTime to)
    {
        var result = new List<CalendarEvent>();
        Dictionary<string, (string Params, string Value)>? props = null;
        var exdates = new HashSet<DateTime>();
        int nested = 0;

        foreach (var line in Unfold(text))
        {
            if (line == "BEGIN:VEVENT")
            {
                props = new();
                exdates.Clear();
                nested = 0;
                continue;
            }
            if (props == null) continue;

            if (line == "END:VEVENT")
            {
                try
                {
                    Expand(props, exdates, from, to, result);
                }
                catch (Exception ex)
                {
                    Log.Write("ics event", ex);
                }
                props = null;
                continue;
            }
            if (line.StartsWith("BEGIN:")) { nested++; continue; }
            if (line.StartsWith("END:")) { nested--; continue; }
            if (nested > 0) continue;

            var (name, prms, value) = SplitLine(line);
            if (name == "EXDATE")
            {
                foreach (var part in value.Split(','))
                    if (ParseDate(prms, part) is { } ex) exdates.Add(ex.Date);
            }
            else
            {
                props[name] = (prms, value);
            }
        }
        return result;
    }

    static void Expand(Dictionary<string, (string Params, string Value)> p, HashSet<DateTime> exdates, DateTime from, DateTime to, List<CalendarEvent> output)
    {
        if (!p.TryGetValue("DTSTART", out var dtStart) || ParseDate(dtStart.Params, dtStart.Value) is not { } start) return;
        if (p.TryGetValue("STATUS", out var status) && status.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) return;

        var length = start.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
        if (p.TryGetValue("DTEND", out var dtEnd) && ParseDate(dtEnd.Params, dtEnd.Value) is { } end && end.Date > start.Date)
            length = end.Date - start.Date;

        var title = p.TryGetValue("SUMMARY", out var summary) ? Unescape(summary.Value) : "(без названия)";
        var location = p.TryGetValue("LOCATION", out var loc) ? Unescape(loc.Value) : "";
        var uid = p.TryGetValue("UID", out var u) ? u.Value : title;

        void Add(DateTime s)
        {
            if (exdates.Contains(s)) return;
            if (s + length > from && s < to) output.Add(new CalendarEvent(uid, title, s, s + length, start.AllDay, location));
        }

        if (!p.TryGetValue("RRULE", out var rrule))
        {
            Add(start.Date);
            return;
        }

        var rule = rrule.Value.Split(';')
            .Select(part => part.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0].ToUpperInvariant(), kv => kv[1]);
        var freq = rule.GetValueOrDefault("FREQ", "");
        int interval = int.TryParse(rule.GetValueOrDefault("INTERVAL"), out var i) && i > 0 ? i : 1;
        int? count = int.TryParse(rule.GetValueOrDefault("COUNT"), out var c) ? c : null;
        DateTime? until = null;
        if (rule.TryGetValue("UNTIL", out var untilText) && ParseDate("", untilText) is { } u2)
            until = u2.AllDay ? u2.Date.AddDays(1).AddTicks(-1) : u2.Date;

        var byDay = new List<int>();
        if (freq == "WEEKLY" && rule.TryGetValue("BYDAY", out var days))
        {
            foreach (var d in days.Split(','))
            {
                int idx = Array.IndexOf(["MO", "TU", "WE", "TH", "FR", "SA", "SU"], d.Trim()[^2..]);
                if (idx >= 0) byDay.Add(idx);
            }
            byDay.Sort();
        }

        int n = 0;
        for (int period = 0; period < 5000; period++)
        {
            DateTime anchor = freq switch
            {
                "DAILY" => start.Date.AddDays(period * interval),
                "WEEKLY" => start.Date.AddDays(7 * period * interval),
                "MONTHLY" => start.Date.AddMonths(period * interval),
                "YEARLY" => start.Date.AddYears(period * interval),
                _ => DateTime.MaxValue,
            };
            if (anchor == DateTime.MaxValue)
            {
                Add(start.Date);
                return;
            }

            IEnumerable<DateTime> occurrences = [anchor];
            if (byDay.Count > 0)
            {
                var monday = anchor.Date.AddDays(-(((int)anchor.DayOfWeek + 6) % 7)) + anchor.TimeOfDay;
                occurrences = byDay.Select(d => monday.AddDays(d)).Where(o => o >= start.Date);
            }

            foreach (var occ in occurrences)
            {
                if (until != null && occ > until) return;
                if (count != null && n >= count) return;
                if (occ > to) return;
                n++;
                Add(occ);
            }
        }
    }

    readonly record struct ParsedDate(DateTime Date, bool AllDay);

    static ParsedDate? ParseDate(string prms, string value)
    {
        value = value.Trim();
        if (value.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return new ParsedDate(day, true);

        bool utc = value.EndsWith('Z');
        if (!DateTime.TryParseExact(value.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return null;
        if (utc) return new ParsedDate(DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(), false);

        var tzid = GetParam(prms, "TZID");
        if (tzid != null)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(tzid);
                return new ParsedDate(TimeZoneInfo.ConvertTime(dt, tz, TimeZoneInfo.Local), false);
            }
            catch
            {
                // неизвестный пояс — считаем локальным временем
            }
        }
        return new ParsedDate(dt, false);
    }

    static string? GetParam(string prms, string name)
    {
        foreach (var part in prms.Split(';'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals(name, StringComparison.OrdinalIgnoreCase)) return kv[1].Trim('"');
        }
        return null;
    }

    static (string Name, string Params, string Value) SplitLine(string line)
    {
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted)
            {
                var head = line[..i];
                var semi = head.IndexOf(';');
                return semi < 0
                    ? (head.ToUpperInvariant(), "", line[(i + 1)..])
                    : (head[..semi].ToUpperInvariant(), head[(semi + 1)..], line[(i + 1)..]);
            }
        }
        return (line.ToUpperInvariant(), "", "");
    }

    static IEnumerable<string> Unfold(string text)
    {
        string? current = null;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                current += raw[1..];
                continue;
            }
            if (current != null) yield return current;
            current = raw;
        }
        if (current != null) yield return current;
    }

    static string Unescape(string s) =>
        s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
