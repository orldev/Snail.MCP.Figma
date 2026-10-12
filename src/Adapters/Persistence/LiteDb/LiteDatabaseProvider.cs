using LiteDB;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Persistence.LiteDb;

/// <summary>Opens LiteDB in the data directory and creates the indexes that invalidation relies on.</summary>
public sealed class LiteDatabaseProvider(ServerConfig config, ILogger<LiteDatabaseProvider> logger)
    : ILiteDatabaseProvider, IDisposable
{
    /// <summary>Pause before retrying to open the database after a failure.</summary>
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private LiteDatabase? _database;
    private DateTimeOffset _nextRetryAt = DateTimeOffset.MinValue;

    public bool IsStarted => _database is not null;

    public string? LastError { get; private set; }

    public LiteDatabase GetDatabase()
    {
        if (_database is not null)
        {
            return _database;
        }

        _gate.Wait();

        try
        {
            if (_database is not null)
            {
                return _database;
            }

            try
            {
                _database = Open();
                LastError = null;

                return _database;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _nextRetryAt = DateTimeOffset.UtcNow + RetryCooldown;

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public LiteDatabase? TryGetDatabase()
    {
        if (_database is not null)
        {
            return _database;
        }

        if (!config.Cache.Enabled)
        {
            return null;
        }

        if (DateTimeOffset.UtcNow < _nextRetryAt)
        {
            return null;
        }

        try
        {
            return GetDatabase();
        }
        catch (Exception ex)
        {
            Log.CacheUnavailable(logger, ex, _nextRetryAt);

            return null;
        }
    }

    private LiteDatabase Open()
    {
        Directory.CreateDirectory(config.DataDirectory);
        RemoveLegacyRavenData();

        var connection = new ConnectionString
        {
            Filename = config.CacheDatabaseFile,
            Connection = config.Cache.SharedConnection ? ConnectionType.Shared : ConnectionType.Direct,
        };

        Log.CacheOpening(logger, config.CacheDatabaseFile);
        var startedAt = DateTimeOffset.UtcNow;

        var database = new LiteDatabase(connection, CreateMapper());

        var nodes = database.GetCollection<NodeSnapshot>(LiteDbCollections.Nodes);
        nodes.EnsureIndex(n => n.FileKey);
        nodes.EnsureIndex(n => n.ExpiresAt);
        database.GetCollection<DocumentVersionRecord>(LiteDbCollections.Versions).EnsureIndex(v => v.FileKey);

        Log.CacheReady(logger, (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, config.CacheDatabaseFile);

        return database;
    }

    /// <summary>Own mapper instead of <c>BsonMapper.Global</c>, so <see cref="DateTimeOffset"/> stays comparable in <c>ExpiresAt</c> queries.</summary>
    private static BsonMapper CreateMapper()
    {
        var mapper = new BsonMapper();

        mapper.RegisterType(
            serialize: value => value.UtcDateTime,
            deserialize: bson => new DateTimeOffset(bson.AsDateTime.ToUniversalTime(), TimeSpan.Zero));

        return mapper;
    }

    /// <summary>Removes the legacy embedded RavenDB store, but only when the directory carries its signature.</summary>
    private void RemoveLegacyRavenData()
    {
        var directory = config.LegacyRavenDirectory;

        try
        {
            if (Directory.Exists(directory) &&
                (Directory.Exists(Path.Combine(directory, "System")) ||
                 Directory.Exists(Path.Combine(directory, "Databases"))))
            {
                Directory.Delete(directory, recursive: true);
                Log.LegacyStoreRemoved(logger, directory);
            }

            var logs = Path.Combine(config.DataDirectory, "logs", "raven");

            if (Directory.Exists(logs))
            {
                Directory.Delete(logs, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Log.LegacyStoreNotRemoved(logger, ex, directory);
        }
    }

    public void Dispose()
    {
        _database?.Dispose();
        _database = null;
        _gate.Dispose();
    }
}
