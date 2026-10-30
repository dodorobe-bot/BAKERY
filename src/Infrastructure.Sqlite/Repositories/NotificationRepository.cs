using System.Globalization;
using Microsoft.Data.Sqlite;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace Seruni22Bakery.Infrastructure.Sqlite.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly DbConnectionFactory _factory;
    public NotificationRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<int> CreateAsync(Notification notification)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        // DedupeKey unik membuat penyegaran berkala idempoten: pengingat yang sama untuk pengguna
        // yang sama tidak menumpuk menjadi puluhan baris.
        cmd.CommandText = @"
            INSERT INTO Notifications
                (EventType, Severity, Title, Body, EntityType, EntityId, RecipientUserId,
                 DedupeKey, IsRead, ReadAt, CreatedAt, MetadataJson)
            VALUES
                (@event, @severity, @title, @body, @entityType, @entityId, @recipient,
                 @dedupe, 0, NULL, @created, @metadata)
            ON CONFLICT(DedupeKey) DO NOTHING;";
        cmd.Parameters.AddWithValue("@event", notification.EventType);
        cmd.Parameters.AddWithValue("@severity", notification.Severity.ToString());
        cmd.Parameters.AddWithValue("@title", notification.Title);
        cmd.Parameters.AddWithValue("@body", notification.Body);
        cmd.Parameters.AddWithValue("@entityType", notification.EntityType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@entityId", notification.EntityId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@recipient", notification.RecipientUserId);
        cmd.Parameters.AddWithValue("@dedupe", notification.DedupeKey);
        cmd.Parameters.AddWithValue("@created", Format(notification.CreatedAt));
        cmd.Parameters.AddWithValue("@metadata", notification.MetadataJson ?? (object)DBNull.Value);

        if (await cmd.ExecuteNonQueryAsync() == 0) return 0;

        var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt32(await idCmd.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<Notification>> ListAsync(int recipientUserId, bool unreadOnly, int limit)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT * FROM Notifications
            WHERE RecipientUserId = @recipient {(unreadOnly ? "AND IsRead = 0" : "")}
            ORDER BY CreatedAt DESC, Id DESC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@recipient", recipientUserId);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Notification>();
        while (await reader.ReadAsync()) list.Add(Map(reader));
        return list;
    }

    public async Task<NotificationSummary> SummaryAsync(int recipientUserId)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN IsRead = 0 THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN IsRead = 0 AND Severity = 'Critical' THEN 1 ELSE 0 END), 0)
            FROM Notifications WHERE RecipientUserId = @recipient;";
        cmd.Parameters.AddWithValue("@recipient", recipientUserId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new NotificationSummary(0, 0, 0);
        return new NotificationSummary(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    public async Task<bool> MarkReadAsync(int id, int recipientUserId, DateTime readAtUtc)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Notifications
            SET IsRead = 1, ReadAt = COALESCE(ReadAt, @at)
            WHERE Id = @id AND RecipientUserId = @recipient;";
        cmd.Parameters.AddWithValue("@at", Format(readAtUtc));
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@recipient", recipientUserId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<int> MarkAllReadAsync(int recipientUserId, DateTime readAtUtc)
    {
        using var conn = _factory.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Notifications
            SET IsRead = 1, ReadAt = COALESCE(ReadAt, @at)
            WHERE RecipientUserId = @recipient AND IsRead = 0;";
        cmd.Parameters.AddWithValue("@at", Format(readAtUtc));
        cmd.Parameters.AddWithValue("@recipient", recipientUserId);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static string Format(DateTime value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTime Parse(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static Notification Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        EventType = r.GetString(r.GetOrdinal("EventType")),
        Severity = Enum.Parse<NotificationSeverity>(r.GetString(r.GetOrdinal("Severity"))),
        Title = r.GetString(r.GetOrdinal("Title")),
        Body = r.GetString(r.GetOrdinal("Body")),
        EntityType = r.IsDBNull(r.GetOrdinal("EntityType")) ? null : r.GetString(r.GetOrdinal("EntityType")),
        EntityId = r.IsDBNull(r.GetOrdinal("EntityId")) ? null : r.GetInt32(r.GetOrdinal("EntityId")),
        RecipientUserId = r.GetInt32(r.GetOrdinal("RecipientUserId")),
        DedupeKey = r.GetString(r.GetOrdinal("DedupeKey")),
        IsRead = r.GetInt32(r.GetOrdinal("IsRead")) != 0,
        ReadAt = r.IsDBNull(r.GetOrdinal("ReadAt")) ? null : Parse(r.GetString(r.GetOrdinal("ReadAt"))),
        CreatedAt = Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        MetadataJson = r.IsDBNull(r.GetOrdinal("MetadataJson")) ? null : r.GetString(r.GetOrdinal("MetadataJson")),
    };
}
