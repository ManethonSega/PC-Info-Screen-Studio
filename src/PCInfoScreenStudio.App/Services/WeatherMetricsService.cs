using System.Net.Http;
using System.Text.Json;

namespace PCInfoScreenStudio.Services;

public sealed class WeatherMetricsService : IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private string? _cachedCity;
    private DateTimeOffset _nextRefreshUtc = DateTimeOffset.MinValue;
    private IReadOnlyDictionary<string, MetricValue> _cachedMetrics =
        new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);

    public string Status { get; private set; } = "Weather city not configured.";

    public WeatherMetricsService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PCInfoScreenStudio/0.5");
    }

    public async Task<IReadOnlyDictionary<string, MetricValue>> GetMetricsAsync(
        string? city,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        city = city?.Trim();

        if (string.IsNullOrWhiteSpace(city))
        {
            Status = "Weather city not configured.";
            _cachedMetrics = new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);
            _cachedCity = null;
            return _cachedMetrics;
        }

        if (!forceRefresh &&
            string.Equals(_cachedCity, city, StringComparison.OrdinalIgnoreCase) &&
            DateTimeOffset.UtcNow < _nextRefreshUtc)
        {
            return _cachedMetrics;
        }

        try
        {
            var location = await ResolveLocationAsync(city, cancellationToken);
            if (location is null)
            {
                Status = $"City not found: {city}";
                return _cachedMetrics;
            }

            var metrics = await FetchCurrentWeatherAsync(location, cancellationToken);
            _cachedCity = city;
            _cachedMetrics = metrics;
            _nextRefreshUtc = DateTimeOffset.UtcNow.AddMinutes(10);
            Status = $"Weather: {location.DisplayName}";

            return _cachedMetrics;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Status = "Weather unavailable: " + ex.GetBaseException().Message;
            return _cachedMetrics;
        }
    }

    private async Task<WeatherLocation?> ResolveLocationAsync(
        string city,
        CancellationToken cancellationToken)
    {
        var url =
            "https://geocoding-api.open-meteo.com/v1/search" +
            $"?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!json.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return null;
        }

        var item = results[0];
        var name = GetString(item, "name") ?? city;
        var admin1 = GetString(item, "admin1");
        var country = GetString(item, "country");

        if (!item.TryGetProperty("latitude", out var latitudeElement) ||
            !item.TryGetProperty("longitude", out var longitudeElement) ||
            !latitudeElement.TryGetDouble(out var latitude) ||
            !longitudeElement.TryGetDouble(out var longitude))
        {
            return null;
        }

        var displayParts = new[] { name, admin1, country }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return new WeatherLocation(
            latitude,
            longitude,
            string.Join(", ", displayParts));
    }

    private async Task<IReadOnlyDictionary<string, MetricValue>> FetchCurrentWeatherAsync(
        WeatherLocation location,
        CancellationToken cancellationToken)
    {
        var currentVariables = string.Join(",",
            "temperature_2m",
            "relative_humidity_2m",
            "apparent_temperature",
            "weather_code",
            "wind_speed_10m",
            "wind_direction_10m",
            "wind_gusts_10m",
            "precipitation",
            "cloud_cover",
            "pressure_msl",
            "is_day");

        var dailyVariables = string.Join(",",
            "temperature_2m_max",
            "temperature_2m_min",
            "precipitation_probability_max",
            "sunrise",
            "sunset",
            "weather_code");

        var url =
            "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $"&longitude={location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $"&current={currentVariables}" +
            $"&daily={dailyVariables}&forecast_days=1&timezone=auto";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!json.RootElement.TryGetProperty("current", out var current))
            throw new InvalidOperationException("Weather provider returned no current conditions.");

        var output = new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Weather.Location"] = new MetricValue(Text: location.DisplayName)
        };

        AddNumber(output, current, "temperature_2m", "Weather.Temperature", "°C");
        AddNumber(output, current, "apparent_temperature", "Weather.FeelsLike", "°C");
        AddNumber(output, current, "relative_humidity_2m", "Weather.Humidity", "%");
        AddNumber(output, current, "wind_speed_10m", "Weather.Wind", " km/h");
        AddNumber(output, current, "wind_direction_10m", "Weather.WindDirection", "°");
        AddNumber(output, current, "wind_gusts_10m", "Weather.WindGust", " km/h");
        AddNumber(output, current, "precipitation", "Weather.Precipitation", " mm");
        AddNumber(output, current, "cloud_cover", "Weather.CloudCover", "%");
        AddNumber(output, current, "pressure_msl", "Weather.Pressure", " hPa");

        if (TryGetDouble(current, "weather_code", out var code))
            output["Weather.Condition"] = new MetricValue(Text: DescribeWeatherCode((int)Math.Round(code)));

        if (TryGetDouble(current, "is_day", out var isDay))
            output["Weather.DayNight"] = new MetricValue(Text: isDay >= 0.5 ? "Day" : "Night");

        if (json.RootElement.TryGetProperty("daily", out var daily))
        {
            AddFirstNumber(output, daily, "temperature_2m_max", "Weather.TodayHigh", "°C");
            AddFirstNumber(output, daily, "temperature_2m_min", "Weather.TodayLow", "°C");
            AddFirstNumber(output, daily, "precipitation_probability_max", "Weather.PrecipitationChance", "%");
            AddFirstTime(output, daily, "sunrise", "Weather.Sunrise");
            AddFirstTime(output, daily, "sunset", "Weather.Sunset");

            if (TryGetFirstDouble(daily, "weather_code", out var dailyCode))
                output["Weather.TodayCondition"] = new MetricValue(Text: DescribeWeatherCode((int)Math.Round(dailyCode)));
        }

        return output;
    }


    private static void AddFirstNumber(
        IDictionary<string, MetricValue> output,
        JsonElement parent,
        string sourceProperty,
        string metricName,
        string unit)
    {
        if (TryGetFirstDouble(parent, sourceProperty, out var value))
            output[metricName] = new MetricValue(value, Unit: unit);
    }

    private static void AddFirstTime(
        IDictionary<string, MetricValue> output,
        JsonElement parent,
        string sourceProperty,
        string metricName)
    {
        if (!parent.TryGetProperty(sourceProperty, out var array) ||
            array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() == 0 ||
            array[0].ValueKind != JsonValueKind.String)
            return;

        var value = array[0].GetString();
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (DateTime.TryParse(value, out var time))
            value = RegionalFormatService.FormatShortTime(time);

        output[metricName] = new MetricValue(Text: value);
    }

    private static bool TryGetFirstDouble(JsonElement parent, string property, out double value)
    {
        value = 0;
        return parent.TryGetProperty(property, out var array) &&
               array.ValueKind == JsonValueKind.Array &&
               array.GetArrayLength() > 0 &&
               array[0].ValueKind == JsonValueKind.Number &&
               array[0].TryGetDouble(out value);
    }

    private static void AddNumber(
        IDictionary<string, MetricValue> output,
        JsonElement current,
        string sourceProperty,
        string metricName,
        string unit)
    {
        if (TryGetDouble(current, sourceProperty, out var value))
            output[metricName] = new MetricValue(value, Unit: unit);
    }

    private static bool TryGetDouble(JsonElement element, string property, out double value)
    {
        value = 0;
        return element.TryGetProperty(property, out var propertyElement) &&
               propertyElement.ValueKind == JsonValueKind.Number &&
               propertyElement.TryGetDouble(out value);
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string DescribeWeatherCode(int code) => code switch
    {
        0 => "Clear",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 => "Snow",
        77 => "Snow grains",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => $"Weather code {code}"
    };

    public void Dispose() => _httpClient.Dispose();

    private sealed record WeatherLocation(
        double Latitude,
        double Longitude,
        string DisplayName);
}
