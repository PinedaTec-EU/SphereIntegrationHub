using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SphereIntegrationHub.Services;

/// <summary>
/// Tracks anonymous usage statistics locally and sends a lightweight ping
/// at most once every <see cref="PingIntervalDays"/> days.
///
/// No personal data is collected. The ping contains only:
///   installId  – random GUID generated on first run (device fingerprint)
///   version    – binary version
///   os         – OS platform + architecture
///   runs       – executions since the last ping
///   daysSince  – days since first recorded execution
///
/// Opt-out: set environment variable SIH_USAGE_PING=0
/// </summary>
internal static class UsagePingService
{
    private const int PingIntervalDays = 7;
    private const int PingTimeoutSeconds = 5;
    private const string PingEndpoint = "https://sih.pinedatec.eu/ping";
    private const string OptOutEnvVar = "SIH_USAGE_PING";
    private const string StateDirName = "sih";
    private const string StateFileName = "usage.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Records this execution and, when the ping interval has elapsed,
    /// fires a background HTTP ping. Returns the background task so the
    /// caller can optionally await it with a timeout before process exit.
    /// Returns null when telemetry is disabled or no ping is due.
    /// </summary>
    internal static Task? RecordAndMaybePing(string version)
    {
        if (IsOptedOut())
            return null;

        try
        {
            var state = LoadState();
            state.RunsSinceLastPing++;

            bool firstRun = state.LastPingUtc == default;
            bool intervalElapsed = (DateTime.UtcNow - state.LastPingUtc).TotalDays >= PingIntervalDays;

            if (!firstRun && !intervalElapsed)
            {
                SaveState(state);
                return null;
            }

            var payload = BuildPayload(state, version);
            state.LastPingUtc = DateTime.UtcNow;
            state.RunsSinceLastPing = 0;
            SaveState(state);

            return Task.Run(() => SendPingAsync(payload));
        }
        catch
        {
            // Never block the CLI due to usage tracking errors.
            return null;
        }
    }

    private static bool IsOptedOut()
    {
        var val = Environment.GetEnvironmentVariable(OptOutEnvVar);
        return val is "0" or "false" or "no";
    }

    private static UsageState LoadState()
    {
        var path = GetStatePath();

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<UsageState>(json, _jsonOptions);
                if (loaded is not null)
                    return loaded;
            }
            catch { /* corrupt file — reset */ }
        }

        return new UsageState
        {
            InstallId = Guid.NewGuid().ToString("N"),
            FirstSeenUtc = DateTime.UtcNow
        };
    }

    private static void SaveState(UsageState state)
    {
        try
        {
            var path = GetStatePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, _jsonOptions), Encoding.UTF8);
        }
        catch { /* silent */ }
    }

    private static string GetStatePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, StateDirName, StateFileName);
    }

    private static PingPayload BuildPayload(UsageState state, string version)
    {
        var daysSinceFirst = state.FirstSeenUtc == default
            ? 0
            : (int)(DateTime.UtcNow - state.FirstSeenUtc).TotalDays;

        var os = $"{RuntimeInformation.OSDescription.Split(' ')[0]}-{RuntimeInformation.OSArchitecture}"
                     .ToLowerInvariant();

        return new PingPayload(
            InstallId: state.InstallId,
            Version: version,
            Os: os,
            Runs: state.RunsSinceLastPing,
            DaysSinceFirst: daysSinceFirst
        );
    }

    private static async Task SendPingAsync(PingPayload payload)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(PingTimeoutSeconds) };
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            await client.PostAsync(PingEndpoint, content).ConfigureAwait(false);
        }
        catch
        {
            // Network errors, firewall blocks, or server downtime must never
            // surface to the user. The state is already persisted; the next
            // eligible run will retry automatically.
        }
    }

    private sealed class UsageState
    {
        public string InstallId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastPingUtc { get; set; }
        public int RunsSinceLastPing { get; set; }
    }

    private sealed record PingPayload(
        string InstallId,
        string Version,
        string Os,
        int Runs,
        int DaysSinceFirst
    );
}
