using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

// ---------------------------------------------------------------------------
// SphereIntegrationHub Telemetry Receiver
// Listens for anonymous usage pings from sih CLI installations.
//
// Endpoints:
//   POST /ping   – receive a usage ping (public)
//   GET  /stats  – aggregated stats (requires SIH_STATS_KEY header/query)
//
// Configuration (environment variables):
//   SIH_DB_PATH      – SQLite file path (default: sih_telemetry.db next to binary)
//   SIH_STATS_KEY    – secret key required to read /stats (required to enable stats)
//   ASPNETCORE_URLS  – bind address (default: http://+:5200)
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<PingRepository>();

var app = builder.Build();

// Ensure DB is ready before accepting requests
app.Services.GetRequiredService<PingRepository>().EnsureCreated();

// ---------------------------------------------------------------------------
// POST /ping
// ---------------------------------------------------------------------------
app.MapPost("/ping", async (HttpContext ctx, PingRepository repo) =>
{
    PingPayload? payload;
    try
    {
        payload = await ctx.Request.ReadFromJsonAsync<PingPayload>();
    }
    catch
    {
        return Results.BadRequest();
    }

    if (payload is null || string.IsNullOrWhiteSpace(payload.InstallId))
        return Results.BadRequest();

    var ipHash = HashIp(ctx.Connection.RemoteIpAddress?.ToString() ?? "");
    repo.Insert(payload, ipHash);

    return Results.Ok();
});

// ---------------------------------------------------------------------------
// GET /stats  (protected)
// ---------------------------------------------------------------------------
app.MapGet("/stats", (HttpContext ctx, PingRepository repo) =>
{
    var statsKey = Environment.GetEnvironmentVariable("SIH_STATS_KEY");

    if (string.IsNullOrWhiteSpace(statsKey))
        return Results.NotFound(); // stats disabled if key not configured

    // Accept key via ?key= query param or Authorization: Bearer <key> header
    var provided = ctx.Request.Query["key"].ToString();
    if (string.IsNullOrWhiteSpace(provided))
    {
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            provided = auth["Bearer ".Length..].Trim();
    }

    if (provided != statsKey)
        return Results.Unauthorized();

    var stats = repo.GetStats();
    return Results.Json(stats);
});

app.Run();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
static string HashIp(string ip)
{
    if (string.IsNullOrWhiteSpace(ip)) return "";
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
    return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
}

// ---------------------------------------------------------------------------
// Models
// ---------------------------------------------------------------------------
record PingPayload(
    [property: JsonPropertyName("installId")]    string InstallId,
    [property: JsonPropertyName("version")]      string? Version,
    [property: JsonPropertyName("os")]           string? Os,
    [property: JsonPropertyName("runs")]         int Runs,
    [property: JsonPropertyName("daysSinceFirst")] int DaysSinceFirst
);

// ---------------------------------------------------------------------------
// Repository
// ---------------------------------------------------------------------------
sealed class PingRepository
{
    private readonly string _dbPath;

    public PingRepository()
    {
        _dbPath = Environment.GetEnvironmentVariable("SIH_DB_PATH")
                  ?? Path.Combine(AppContext.BaseDirectory, "sih_telemetry.db");
    }

    public void EnsureCreated()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS pings (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                install_id       TEXT    NOT NULL,
                version          TEXT,
                os               TEXT,
                runs             INTEGER NOT NULL DEFAULT 0,
                days_since_first INTEGER NOT NULL DEFAULT 0,
                received_at_utc  TEXT    NOT NULL,
                ip_hash          TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_install_id ON pings (install_id);
            CREATE INDEX IF NOT EXISTS idx_received_at ON pings (received_at_utc);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Insert(PingPayload p, string ipHash)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pings (install_id, version, os, runs, days_since_first, received_at_utc, ip_hash)
            VALUES ($install_id, $version, $os, $runs, $days_since_first, $received_at, $ip_hash);
            """;
        cmd.Parameters.AddWithValue("$install_id",       p.InstallId);
        cmd.Parameters.AddWithValue("$version",          (object?)p.Version          ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$os",               (object?)p.Os               ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$runs",             p.Runs);
        cmd.Parameters.AddWithValue("$days_since_first", p.DaysSinceFirst);
        cmd.Parameters.AddWithValue("$received_at",      DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$ip_hash",          (object?)ipHash             ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public object GetStats()
    {
        using var conn = Open();

        return new
        {
            uniqueInstallations = ScalarLong(conn, "SELECT COUNT(DISTINCT install_id) FROM pings"),
            totalPings          = ScalarLong(conn, "SELECT COUNT(*) FROM pings"),
            firstPingUtc        = ScalarStr(conn,  "SELECT MIN(received_at_utc) FROM pings"),
            lastPingUtc         = ScalarStr(conn,  "SELECT MAX(received_at_utc) FROM pings"),
            byVersion           = QueryDict(conn,
                "SELECT version, COUNT(DISTINCT install_id) FROM pings GROUP BY version ORDER BY 2 DESC"),
            byOs                = QueryDict(conn,
                "SELECT os, COUNT(DISTINCT install_id) FROM pings GROUP BY os ORDER BY 2 DESC"),
            last30Days          = QueryDailyActivity(conn)
        };
    }

    private static long ScalarLong(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    private static string? ScalarStr(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString();
    }

    private static Dictionary<string, long> QueryDict(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = new Dictionary<string, long>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.IsDBNull(0) ? "unknown" : reader.GetString(0)] = reader.GetInt64(1);
        return result;
    }

    private static List<object> QueryDailyActivity(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DATE(received_at_utc) AS day,
                   COUNT(DISTINCT install_id) AS unique_installs,
                   COUNT(*) AS pings
            FROM pings
            WHERE received_at_utc >= DATE('now', '-30 days')
            GROUP BY day
            ORDER BY day DESC;
            """;
        var rows = new List<object>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rows.Add(new { day = reader.GetString(0), uniqueInstalls = reader.GetInt64(1), pings = reader.GetInt64(2) });
        return rows;
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }
}
