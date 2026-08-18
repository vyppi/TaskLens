using Microsoft.Data.Sqlite;
using System.Globalization;

namespace TaskLens.Core;

public sealed class SqliteTaskRepository(string databasePath) : ITaskRepository
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(Path.GetFullPath(databasePath))!);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Areas (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Color TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_Areas_Name
            ON Areas (Name COLLATE NOCASE);

            CREATE TABLE IF NOT EXISTS Tasks (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                Notes TEXT NOT NULL,
                AreaId TEXT NOT NULL,
                DueAt TEXT NULL,
                EstimatedMinutes INTEGER NOT NULL,
                Priority INTEGER NOT NULL,
                IsCompleted INTEGER NOT NULL,
                Source INTEGER NOT NULL,
                SourceExcerpt TEXT NOT NULL,
                Rationale TEXT NOT NULL,
                Confidence REAL NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (AreaId) REFERENCES Areas(Id)
            );

            DELETE FROM Areas
            WHERE Id IN ('blue-badge', 'ai-certification', 'manager', 'personal')
              AND NOT EXISTS (SELECT 1 FROM Tasks WHERE Tasks.AreaId = Areas.Id);

            INSERT OR IGNORE INTO Areas (Id, Name, Color)
            VALUES ('general', 'General', '#2563EB');
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkArea>> GetAreasAsync(
        CancellationToken cancellationToken = default)
    {
        var areas = new List<WorkArea>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Color FROM Areas ORDER BY rowid;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            areas.Add(new WorkArea(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return areas;
    }

    public async Task CreateAreaAsync(
        WorkArea area,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(area.Name);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Areas (Id, Name, Color) VALUES ($id, $name, $color);";
        command.Parameters.AddWithValue("$id", area.Id);
        command.Parameters.AddWithValue("$name", area.Name.Trim());
        command.Parameters.AddWithValue("$color", area.Color);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAreaAsync(
        string areaId,
        string? replacementAreaId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(areaId);
        if (replacementAreaId == areaId)
        {
            throw new ArgumentException(
                "The replacement area must be different.",
                nameof(replacementAreaId));
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        var countCommand = connection.CreateCommand();
        countCommand.Transaction = (SqliteTransaction)transaction;
        countCommand.CommandText =
            "SELECT COUNT(*) FROM Tasks WHERE AreaId = $areaId;";
        countCommand.Parameters.AddWithValue("$areaId", areaId);
        var taskCount = Convert.ToInt32(
            await countCommand.ExecuteScalarAsync(cancellationToken));

        if (taskCount > 0 && string.IsNullOrWhiteSpace(replacementAreaId))
        {
            throw new InvalidOperationException(
                "The area contains tasks and requires a replacement area.");
        }

        if (taskCount > 0)
        {
            var moveCommand = connection.CreateCommand();
            moveCommand.Transaction = (SqliteTransaction)transaction;
            moveCommand.CommandText =
                "UPDATE Tasks SET AreaId = $replacementAreaId WHERE AreaId = $areaId;";
            moveCommand.Parameters.AddWithValue("$replacementAreaId", replacementAreaId!);
            moveCommand.Parameters.AddWithValue("$areaId", areaId);
            await moveCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = (SqliteTransaction)transaction;
        deleteCommand.CommandText = "DELETE FROM Areas WHERE Id = $areaId;";
        deleteCommand.Parameters.AddWithValue("$areaId", areaId);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<TaskItem>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Title, Notes, AreaId, DueAt, Priority,
                   IsCompleted, Source, SourceExcerpt, Rationale, Confidence, CreatedAt
            FROM Tasks
            ORDER BY IsCompleted, COALESCE(DueAt, '9999-12-31'), Priority DESC, CreatedAt DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(new TaskItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                ReadDate(reader, 4),
                (TaskPriority)reader.GetInt32(5),
                reader.GetBoolean(6),
                (TaskSource)reader.GetInt32(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetDouble(10),
                ReadRequiredDate(reader, 11)));
        }

        return tasks;
    }

    public async Task SaveTaskAsync(
        TaskItem task,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(task.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(task.Title);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Tasks (
                Id, Title, Notes, AreaId, DueAt, EstimatedMinutes, Priority,
                IsCompleted, Source, SourceExcerpt, Rationale, Confidence, CreatedAt)
            VALUES (
                $id, $title, $notes, $areaId, $dueAt, $estimatedMinutes, $priority,
                $isCompleted, $source, $sourceExcerpt, $rationale, $confidence, $createdAt)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Notes = excluded.Notes,
                AreaId = excluded.AreaId,
                DueAt = excluded.DueAt,
                EstimatedMinutes = excluded.EstimatedMinutes,
                Priority = excluded.Priority,
                IsCompleted = excluded.IsCompleted,
                Source = excluded.Source,
                SourceExcerpt = excluded.SourceExcerpt,
                Rationale = excluded.Rationale,
                Confidence = excluded.Confidence;
            """;
        command.Parameters.AddWithValue("$id", task.Id);
        command.Parameters.AddWithValue("$title", task.Title);
        command.Parameters.AddWithValue("$notes", task.Notes);
        command.Parameters.AddWithValue("$areaId", task.AreaId);
        command.Parameters.AddWithValue(
            "$dueAt",
            task.DueAt?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$estimatedMinutes", 0);
        command.Parameters.AddWithValue("$priority", (int)task.Priority);
        command.Parameters.AddWithValue("$isCompleted", task.IsCompleted);
        command.Parameters.AddWithValue("$source", (int)task.Source);
        command.Parameters.AddWithValue("$sourceExcerpt", task.SourceExcerpt);
        command.Parameters.AddWithValue("$rationale", task.Rationale);
        command.Parameters.AddWithValue("$confidence", task.Confidence);
        command.Parameters.AddWithValue(
            "$createdAt",
            task.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Tasks WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", taskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DateTimeOffset? ReadDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(
                reader.GetString(ordinal),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);

    private static DateTimeOffset ReadRequiredDate(
        SqliteDataReader reader,
        int ordinal) =>
        DateTimeOffset.Parse(
            reader.GetString(ordinal),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
}
