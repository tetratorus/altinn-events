#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Npgsql;

namespace Altinn.Platform.Events.IntegrationTests.Utils;

/// <summary>
/// Utility methods for working with PostgreSQL in integration tests.
/// </summary>
public static class PostgresTestUtils
{
    /// <summary>
    /// Retrieves an event from the database by its ID.
    /// Retries multiple times to allow for transaction commit and visibility.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="eventId">The ID of the event to retrieve.</param>
    /// <param name="maxAttempts">Maximum number of attempts before giving up. Defaults to 30.</param>
    /// <param name="delayMs">Delay in milliseconds between attempts. Defaults to 500ms.</param>
    /// <returns>The event as a JsonDocument, or null if not found.</returns>
    public static async Task<JsonDocument?> GetEventFromDatabaseAsync(
        string connectionString,
        string eventId,
        int maxAttempts = 30,
        int delayMs = 500)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);

        return await WaitForUtils.WaitForAsync(
            async () =>
            {
                await using var command = dataSource.CreateCommand(
                    "SELECT cloudevent FROM events.events WHERE cloudevent->>'id' = $1");
                command.Parameters.AddWithValue(eventId);

                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var json = reader.GetString(0);
                    return JsonDocument.Parse(json);
                }

                return null;
            },
            maxAttempts,
            delayMs);
    }

    /// <summary>
    /// Retrieves the trace log activities and response codes logged for a cloud event, in insertion order.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="cloudEventId">The ID of the cloud event.</param>
    /// <returns>The activity and response code of each trace log entry for the cloud event.</returns>
    public static async Task<List<(string Activity, int? ResponseCode)>> GetTraceLogEntriesAsync(string connectionString, string cloudEventId)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT activity, responsecode FROM events.trace_log WHERE cloudeventid = $1::uuid ORDER BY sequenceno");
        command.Parameters.AddWithValue(cloudEventId);

        var entries = new List<(string Activity, int? ResponseCode)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1)));
        }

        return entries;
    }
}
