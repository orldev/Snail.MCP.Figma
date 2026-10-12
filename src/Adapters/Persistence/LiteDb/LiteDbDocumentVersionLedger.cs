using LiteDB;
using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Adapters.Persistence.LiteDb;

/// <summary>Remembers the last known file version taken from the read envelope, which costs no extra request.</summary>
public sealed class LiteDbDocumentVersionLedger(
    ILiteDatabaseProvider databaseProvider,
    INodeSnapshotStore cache,
    IRenderSnapshotStore images,
    ILogger<LiteDbDocumentVersionLedger> logger)
    : IDocumentVersionLedger
{
    /// <summary>Records the version that came with read data; a change drops the file's other snapshots, so call it before caching the fresh one.</summary>
    public async Task RecordAsync(string fileKey, DocumentVersion info, CancellationToken cancellationToken = default)
    {
        var database = databaseProvider.TryGetDatabase();

        if (database is null)
        {
            return;
        }

        try
        {
            var versions = Versions(database);
            var id = LiteDbCollections.VersionKey(fileKey);
            var stored = versions.FindById(id);

            if (stored?.Version is not null && info.Version is not null && stored.Version != info.Version)
            {
                Log.FileChanged(logger, fileKey, stored.Version, info.Version);
                await cache.InvalidateFileAsync(fileKey, cancellationToken).ConfigureAwait(false);
                await images.InvalidateFileAsync(fileKey, cancellationToken).ConfigureAwait(false);
            }

            versions.Upsert(new DocumentVersionRecord
            {
                Id = id,
                FileKey = fileKey,
                Version = info.Version,
                LastModified = info.LastModified,
                Name = info.Name,
                CheckedAt = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            Log.VersionNotStored(logger, ex, fileKey);
        }
    }

    /// <summary>Last remembered version — for diagnostics and tool responses.</summary>
    public DocumentVersionRecord? GetStored(string fileKey)
    {
        var database = databaseProvider.TryGetDatabase();

        if (database is null)
        {
            return null;
        }

        try
        {
            return Versions(database).FindById(LiteDbCollections.VersionKey(fileKey));
        }
        catch (Exception ex)
        {
            Log.VersionNotRead(logger, ex, fileKey);

            return null;
        }
    }

    /// <summary>Forgets the remembered version; after a plugin mutation it must be re-read.</summary>
    public Task ForgetAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        var database = databaseProvider.TryGetDatabase();

        if (database is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            Versions(database).Delete(LiteDbCollections.VersionKey(fileKey));
        }
        catch (Exception ex)
        {
            Log.VersionNotForgotten(logger, ex, fileKey);
        }

        return Task.CompletedTask;
    }

    private static ILiteCollection<DocumentVersionRecord> Versions(LiteDatabase database) =>
        database.GetCollection<DocumentVersionRecord>(LiteDbCollections.Versions);
}
