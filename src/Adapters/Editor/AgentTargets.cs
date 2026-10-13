namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>The file each agent of a shared server worked in, by the agent's name, kept on disk so a restart does not make every agent call figma_navigate again.</summary>
/// <remarks>
/// A shared server that restarts drops every session; the clients open new ones and come back as new agents. An agent
/// named after its project folder is the same agent as before, so it gets its file back. Only a recent record or a
/// file that is connected right now is believed: a record from yesterday would send a new session into a file the human
/// has long closed, where the only open file would have been the right guess.
/// </remarks>
public sealed class AgentTargets(string path, ILogger logger)
{
    /// <summary>How old a record may be and still be believed while its file is not connected.</summary>
    public static readonly TimeSpan BelievedFor = TimeSpan.FromHours(1);

    private readonly object _gate = new();

    private JsonObject? _records;

    /// <summary>Writes where the agent works now; no file means the agent has no target.</summary>
    public void Remember(string agent, string? fileKey, string? fileName, DateTimeOffset at)
    {
        lock (_gate)
        {
            var records = Load();

            if (fileKey is null)
            {
                records.Remove(agent);
            }
            else
            {
                records[agent] = new JsonObject
                {
                    ["fileKey"] = fileKey,
                    ["fileName"] = fileName,
                    ["at"] = at.ToString("O"),
                };
            }

            Save(records);
        }
    }

    /// <summary>The file the agent worked in, when the record is young enough or <paramref name="isConnected"/> says the file is here now.</summary>
    public (string FileKey, string? FileName)? Recall(string agent, DateTimeOffset now, Func<string, bool> isConnected)
    {
        lock (_gate)
        {
            if (Load()[agent] is not JsonObject record ||
                record["fileKey"] is not JsonValue key || !key.TryGetValue<string>(out var fileKey) ||
                record["at"] is not JsonValue stamp || !stamp.TryGetValue<string>(out var written) ||
                !DateTimeOffset.TryParse(written, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
            {
                return null;
            }

            if (now - at > BelievedFor && !isConnected(fileKey))
            {
                return null;
            }

            var fileName = record["fileName"] is JsonValue name && name.TryGetValue<string>(out var called) ? called : null;

            return (fileKey, fileName);
        }
    }

    private JsonObject Load()
    {
        if (_records is not null)
        {
            return _records;
        }

        try
        {
            _records = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.AgentTargetsUnreadable(logger, ex, path);
            _records = [];
        }

        return _records;
    }

    /// <summary>Writes beside the file and moves it over, so a crash in the middle leaves the old record rather than half a new one.</summary>
    private void Save(JsonObject records)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var written = $"{path}.tmp";
            File.WriteAllText(written, records.ToJsonString());
            File.Move(written, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.AgentTargetsNotSaved(logger, ex, path);
        }
    }
}
