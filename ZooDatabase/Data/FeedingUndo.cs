using MySqlConnector;

namespace ZooDatabase.Data;

// Recent feedings for an animal, and undoing one that was logged by mistake.
// There is no delete trigger on Feeding_Log, so undo reverses what the insert triggers did:
//   trg_feeding_consumption  took the amount out of feed stock  -> put it back
//   trg_feeding_problem      made make-up / welfare tasks       -> cancel them
public static class FeedingUndo
{
    public record Entry(int LogId, DateTime Date, string Time, string Feed, string Status,
                        decimal? Amount, string Unit, string? By, string? Notes);

    public static async Task<List<Entry>> RecentAsync(MySqlConnection conn, int animalId, int limit = 8)
    {
        var list = new List<Entry>();
        await using var cmd = new MySqlCommand(@"
            SELECT fl.Feeding_Log_ID, fl.Feeding_Date, fs.Feeding_Time, f.Feed_Name, fl.Status,
                   CASE WHEN fl.Status IN ('Completed', 'Partial') THEN COALESCE(fl.Quantity_Given, fs.Quantity) END,
                   fs.Unit, CONCAT(e.First_Name, ' ', e.Last_Name), fl.Notes
            FROM Feeding_Log fl
            JOIN Feeding_Schedule fs ON fs.Schedule_ID = fl.Schedule_ID
            JOIN Feed f ON f.Feed_ID = fs.Feed_ID
            LEFT JOIN Employee e ON e.Employee_ID = fl.Recorded_By_Employee_ID
            WHERE fs.Animal_ID = @animal
            ORDER BY fl.Feeding_Date DESC, fs.Feeding_Time DESC, fl.Feeding_Log_ID DESC
            LIMIT @limit", conn);
        cmd.Parameters.AddWithValue("@animal", animalId);
        cmd.Parameters.AddWithValue("@limit", limit);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new Entry(r.GetInt32(0), r.GetDateTime(1), DateTime.Today.Add(r.GetTimeSpan(2)).ToString("h:mm tt"),
                r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetDecimal(5), r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8)));
        return list;
    }

    // Removes one feeding log. animalId guards against undoing another animal's log.
    // Returns a message for the user, or null if the log wasn't found.
    public static async Task<string?> UndoAsync(MySqlConnection conn, int logId, int? animalId = null)
    {
        await using var tx = await conn.BeginTransactionAsync();

        int feedId;
        string status, animal, feed, unit, time;
        decimal used;
        DateTime date;
        await using (var cmd = new MySqlCommand(@"
            SELECT fs.Feed_ID, fl.Status, COALESCE(fl.Quantity_Given, fs.Quantity), a.Animal_Name, f.Feed_Name, fs.Unit,
                   fs.Feeding_Time, fl.Feeding_Date
            FROM Feeding_Log fl
            JOIN Feeding_Schedule fs ON fs.Schedule_ID = fl.Schedule_ID
            JOIN Animal a ON a.Animal_ID = fs.Animal_ID
            JOIN Feed f ON f.Feed_ID = fs.Feed_ID
            WHERE fl.Feeding_Log_ID = @log AND (@animal IS NULL OR fs.Animal_ID = @animal)
            FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue("@log", logId);
            cmd.Parameters.AddWithValue("@animal", (object?)animalId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            feedId = r.GetInt32(0);
            status = r.GetString(1);
            used = r.GetDecimal(2);
            animal = r.GetString(3);
            feed = r.GetString(4);
            unit = r.GetString(5);
            time = DateTime.Today.Add(r.GetTimeSpan(6)).ToString("h:mm tt");
            date = r.GetDateTime(7);
        }

        async Task<int> Run(string sql)
        {
            await using var cmd = new MySqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@log", logId);
            cmd.Parameters.AddWithValue("@feed", feedId);
            cmd.Parameters.AddWithValue("@qty", used);
            return await cmd.ExecuteNonQueryAsync();
        }

        bool restocked = status is "Completed" or "Partial";
        if (restocked)
            await Run("UPDATE Feed SET Quantity_In_Stock = COALESCE(Quantity_In_Stock, 0) + @qty WHERE Feed_ID = @feed");

        int tasks = await Run(@"
            UPDATE Care_Task SET Status = 'Cancelled', Completed_At = NOW()
            WHERE Source_Type = 'Feeding_Log' AND Source_ID = @log AND Status IN ('Open', 'In Progress')");

        await Run("DELETE FROM Feeding_Log WHERE Feeding_Log_ID = @log");
        await tx.CommitAsync();

        var msg = $"Undid {animal}'s {time} feeding on {date:MMM d} ({status}).";
        if (restocked) msg += $" {used:0.##} {unit} of {feed} was put back in stock.";
        if (tasks > 0) msg += $" {tasks} follow-up task(s) it created were cancelled.";
        return msg + " You can log it again now.";
    }
}
