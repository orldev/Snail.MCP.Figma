namespace Snail.MCP.Figma.Domain;

/// <summary>Fixed-capacity ring buffer; the plugin streams events and an unbounded session would eat all memory.</summary>
public sealed class RingBuffer<T>(int capacity)
{
    private readonly Queue<T> _items = new(capacity);
    private readonly object _gate = new();

    public int Capacity { get; } = capacity;

    public int Count
    {
        get { lock (_gate) { return _items.Count; } }
    }

    public void Add(T item)
    {
        lock (_gate)
        {
            if (_items.Count >= Capacity)
            {
                _items.Dequeue();
            }

            _items.Enqueue(item);
        }
    }

    /// <summary>Last <paramref name="count"/> items, oldest first.</summary>
    public IReadOnlyList<T> Tail(int count)
    {
        lock (_gate)
        {
            return [.. _items.Skip(Math.Max(0, _items.Count - count))];
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }
    }
}
