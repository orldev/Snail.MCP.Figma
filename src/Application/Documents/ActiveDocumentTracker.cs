namespace Snail.MCP.Figma.Application.Documents;

/// <summary>Last file worked with, so <c>fileUrl</c> can be omitted in subsequent calls.</summary>
public sealed class ActiveDocumentTracker
{
    private readonly object _gate = new();
    private string? _fileKey;
    private string? _fileName;

    public string? FileKey
    {
        get { lock (_gate) { return _fileKey; } }
    }

    public string? FileName
    {
        get { lock (_gate) { return _fileName; } }
    }

    public void Remember(string fileKey, string? fileName = null)
    {
        lock (_gate)
        {
            _fileKey = fileKey;

            if (fileName is not null)
            {
                _fileName = fileName;
            }
        }
    }

    /// <summary>File key from the URL, or from the remembered context; throws a readable message when neither is known.</summary>
    public string Resolve(string? fileUrl)
    {
        if (!string.IsNullOrWhiteSpace(fileUrl))
        {
            var parsed = DocumentLocator.Parse(fileUrl);

            if (parsed is null)
            {
                throw new ArgumentException(
                    $"This does not look like a Figma file link: {fileUrl}. Expected the form https://www.figma.com/design/KEY/Name",
                    nameof(fileUrl));
            }

            Remember(parsed.EffectiveKey);

            return parsed.EffectiveKey;
        }

        return FileKey ?? throw new InvalidOperationException(
            "No Figma file is selected: pass fileUrl or call any tool with a link first.");
    }
}
