using System.Globalization;
using System.Text.Json;

namespace DynamicIsland;

public sealed record WeatherInfo(double Temperature, int Code, string City);

public sealed record GeoPlace(string Name, string Region, string Country, double Latitude, double Longitude)
{
    public string Details => string.Join(", ", new[] { Region, Country }.Where(s => !string.IsNullOrWhiteSpace(s) && s != Name));
}

/// <summary>Погода с open-meteo.com — без ключей и регистрации.</summary>
public sealed class WeatherService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    (string Query, GeoPlace Place)? _geoCache;

    public async Task<List<GeoPlace>> SearchAsync(string query, int count = 6)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count={count}&language=ru&format=json";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var list = new List<GeoPlace>();
        if (!doc.RootElement.TryGetProperty("results", out var results)) return list;
        foreach (var r in results.EnumerateArray())
        {
            list.Add(new GeoPlace(
                Str(r, "name") ?? query,
                Str(r, "admin1") ?? "",
                Str(r, "country") ?? "",
                r.GetProperty("latitude").GetDouble(),
                r.GetProperty("longitude").GetDouble()));
        }
        return list;
    }

    public async Task<WeatherInfo?> GetAsync(Settings settings)
    {
        try
        {
            double lat, lon;
            if (settings.Latitude is { } la && settings.Longitude is { } lo)
            {
                lat = la;
                lon = lo;
            }
            else
            {
                if (_geoCache?.Query != settings.City)
                {
                    var found = await SearchAsync(settings.City, 1);
                    if (found.Count == 0) return null;
                    _geoCache = (settings.City, found[0]);
                }
                lat = _geoCache.Value.Place.Latitude;
                lon = _geoCache.Value.Place.Longitude;
            }

            var unit = settings.TemperatureUnit == "fahrenheit" ? "&temperature_unit=fahrenheit" : "";
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code{unit}");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var current = doc.RootElement.GetProperty("current");
            return new WeatherInfo(
                current.GetProperty("temperature_2m").GetDouble(),
                current.GetProperty("weather_code").GetInt32(),
                settings.City);
        }
        catch (Exception ex)
        {
            Log.Write("weather", ex);
            return null;
        }
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static (string Icon, string Text) Describe(int code) => code switch
    {
        0 => ("☀", "Ясно"),
        1 or 2 => ("⛅", "Переменная облачность"),
        3 => ("☁", "Пасмурно"),
        45 or 48 => ("🌫", "Туман"),
        >= 51 and <= 57 => ("🌦", "Морось"),
        >= 61 and <= 67 => ("🌧", "Дождь"),
        >= 71 and <= 77 => ("❄", "Снег"),
        >= 80 and <= 82 => ("🌧", "Ливень"),
        85 or 86 => ("🌨", "Снегопад"),
        >= 95 => ("⛈", "Гроза"),
        _ => ("🌡", "—"),
    };
}
