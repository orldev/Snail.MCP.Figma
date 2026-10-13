namespace Snail.MCP.Figma.Domain;

/// <summary>Fixed-capacity ring buffer; the plugin streams events and an unbounded session would eat all memory.</summary>
public sealed class RingBuffer<T>(int capacity)
{
    private readonly Queue<T> _items = new(capacity);
    private readonly object _gate = new();
    private long _added;

    public int Capacity { get; } = capacity;

    public int Count
    {
        get { lock (_gate) { return _items.Count; } }
    }

    /// <summary>How many items were ever added; a reader keeps this as its mark, because <see cref="Count"/> stops growing once the buffer is full.</summary>
    public long Added
    {
        get { lock (_gate) { return _added; } }
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
            _added++;
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

    /// <summary>Items added after <paramref name="mark"/> (a value of <see cref="Added"/>) that are still held, oldest first.</summary>
    public IReadOnlyList<T> Since(long mark)
    {
        lock (_gate)
        {
            var newer = (int)Math.Clamp(_added - mark, 0, _items.Count);

            return [.. _items.Skip(_items.Count - newer)];
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
