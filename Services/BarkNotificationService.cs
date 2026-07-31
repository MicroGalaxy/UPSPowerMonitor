using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using UPSPowerMonitor.Models;

namespace UPSPowerMonitor.Services;

public sealed class BarkNotificationException(string message) : Exception(message);

public sealed class BarkNotificationService
{
    private static readonly Uri PushEndpoint = new("https://api.day.app/push");
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task SendAsync(
        AppSettings settings,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        var deviceKeys = settings.BarkDeviceKeys
            .Select(key => key.Trim())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (deviceKeys.Length == 0)
        {
            throw new BarkNotificationException("尚未配置 Bark 推送 ID。");
        }

        var payload = new BarkPushPayload
        {
            DeviceKeys = deviceKeys,
            Title = title,
            Body = body,
            Group = string.IsNullOrWhiteSpace(settings.MessageGroup) ? null : settings.MessageGroup.Trim(),
            Call = settings.ContinuousRinging ? "1" : null,
            Level = settings.CriticalAlert ? "critical" : "active"
        };

        using var response = await HttpClient.PostAsJsonAsync(
            PushEndpoint,
            payload,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new BarkNotificationException($"Bark 服务返回 HTTP {(int)response.StatusCode}。");
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("code", out var codeElement)
                && codeElement.TryGetInt32(out var code)
                && code != 200)
            {
                var message = document.RootElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                throw new BarkNotificationException(message ?? $"Bark 推送失败，错误码 {code}。");
            }
        }
        catch (JsonException)
        {
            throw new BarkNotificationException("Bark 服务返回了无法识别的数据。");
        }
    }

    private sealed class BarkPushPayload
    {
        [JsonPropertyName("device_keys")]
        public required string[] DeviceKeys { get; init; }

        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("body")]
        public required string Body { get; init; }

        [JsonPropertyName("group")]
        public string? Group { get; init; }

        [JsonPropertyName("call")]
        public string? Call { get; init; }

        [JsonPropertyName("level")]
        public required string Level { get; init; }
    }
}
