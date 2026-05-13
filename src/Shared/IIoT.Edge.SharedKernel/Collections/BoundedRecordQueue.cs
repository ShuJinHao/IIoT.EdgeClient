using System.Collections;
using System.Collections.Concurrent;

namespace IIoT.Edge.SharedKernel.Collections;

public sealed class BoundedRecordQueue<T> : IReadOnlyCollection<T>
{
    private readonly ConcurrentQueue<T> _queue = new();
    private readonly int _capacity;
    private int _count;

    public BoundedRecordQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "队列容量必须大于 0。");
        }

        _capacity = capacity;
    }

    public int Count => Volatile.Read(ref _count);

    public void Enqueue(T item)
    {
        _queue.Enqueue(item);
        Interlocked.Increment(ref _count);
        Trim();
    }

    public T[] ToArray()
        => _queue.ToArray();

    public IEnumerator<T> GetEnumerator()
        => _queue.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    private void Trim()
    {
        while (Volatile.Read(ref _count) > _capacity && _queue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
        }
    }
}
