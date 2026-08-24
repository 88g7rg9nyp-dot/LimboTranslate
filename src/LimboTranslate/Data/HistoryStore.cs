using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LimboTranslate.Data;

public class HistoryStore
{
    private readonly string _connectionString;

    public HistoryStore()
        : this(DefaultDatabasePath())
    {
    }

    public HistoryStore(string databasePath)
    {
        DatabasePath = databasePath;

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        EnsureSchema();
    }

    public string DatabasePath { get; }

    public static string DefaultDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "LimboTranslate", "history.db");
    }

    public void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            @"CREATE TABLE IF NOT EXISTS history(
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  source_text TEXT NOT NULL,
                  translated_text TEXT NOT NULL,
                  source_lang TEXT,
                  target_lang TEXT,
                  provider TEXT,
                  created_at TEXT NOT NULL);
              CREATE INDEX IF NOT EXISTS idx_history_created_at ON history(created_at DESC);";
        command.ExecuteNonQuery();
    }

    public void Add(HistoryEntry entry)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            @"INSERT INTO history(source_text, translated_text, source_lang, target_lang, provider, created_at)
              VALUES(@source_text, @translated_text, @source_lang, @target_lang, @provider, @created_at);
              SELECT last_insert_rowid();";
        command.Parameters.Add(new SqliteParameter("@source_text", entry.SourceText ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@translated_text", entry.TranslatedText ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@source_lang", (object?)entry.SourceLang ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("@target_lang", (object?)entry.TargetLang ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("@provider", (object?)entry.Provider ?? DBNull.Value));
        command.Parameters.Add(new SqliteParameter("@created_at", FormatDate(entry.CreatedAt)));

        var id = command.ExecuteScalar();
        if (id != null && id != DBNull.Value)
        {
            entry.Id = Convert.ToInt64(id, CultureInfo.InvariantCulture);
        }
    }

    public List<HistoryEntry> Recent(int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT id, source_text, translated_text, source_lang, target_lang, provider, created_at
              FROM history
              ORDER BY created_at DESC, id DESC
              LIMIT @limit;";
        command.Parameters.Add(new SqliteParameter("@limit", limit));

        return ReadEntries(command);
    }

    public List<HistoryEntry> Search(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Recent(limit);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT id, source_text, translated_text, source_lang, target_lang, provider, created_at
              FROM history
              WHERE source_text LIKE @pattern OR translated_text LIKE @pattern
              ORDER BY created_at DESC, id DESC
              LIMIT @limit;";
        command.Parameters.Add(new SqliteParameter("@pattern", "%" + query.Trim() + "%"));
        command.Parameters.Add(new SqliteParameter("@limit", limit));

        return ReadEntries(command);
    }

    public void Delete(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM history WHERE id = @id;";
        command.Parameters.Add(new SqliteParameter("@id", id));
        command.ExecuteNonQuery();
    }

    public void Clear()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM history;";
        command.ExecuteNonQuery();
    }

    public void Trim(int keepCount)
    {
        if (keepCount <= 0)
        {
            Clear();
            return;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            @"DELETE FROM history
              WHERE id NOT IN (
                  SELECT id FROM history
                  ORDER BY created_at DESC, id DESC
                  LIMIT @keep);";
        command.Parameters.Add(new SqliteParameter("@keep", keepCount));
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static List<HistoryEntry> ReadEntries(SqliteCommand command)
    {
        var result = new List<HistoryEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new HistoryEntry
            {
                Id = reader.GetInt64(0),
                SourceText = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                TranslatedText = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                SourceLang = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                TargetLang = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Provider = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                CreatedAt = ParseDate(reader.IsDBNull(6) ? null : reader.GetString(6))
            });
        }

        return result;
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }

    private static DateTime ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return DateTime.UtcNow;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return DateTime.UtcNow;
    }
}
