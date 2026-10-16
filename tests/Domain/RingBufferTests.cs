namespace Snail.MCP.Figma.Tests.Domain;

/// <summary>The buffer behind a file's console: a reader marks how far it has read by the count of everything ever added, because the number held stops moving once the buffer is full.</summary>
public class RingBufferTests
{
    [Fact]
    public void RingBuffer_Full_KeepsCountingWhatWasAdded()
    {
        var buffer = new RingBuffer<int>(3);

        foreach (var item in Enumerable.Range(1, 5))
        {
            buffer.Add(item);
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal(5, buffer.Added);
    }

    /// <summary>The defect it replaces: a reader that compared counts saw nothing new once the console held its thousand lines.</summary>
    [Fact]
    public void RingBuffer_FullWhenMarked_SeesWhatArrivedAfterTheMark()
    {
        var buffer = new RingBuffer<int>(3);

        foreach (var item in Enumerable.Range(1, 3))
        {
            buffer.Add(item);
        }

        var mark = buffer.Added;
        buffer.Add(4);
        buffer.Add(5);

        Assert.Equal([4, 5], buffer.Since(mark));
    }

    [Fact]
    public void RingBuffer_MarkOlderThanItsContents_ReturnsOnlyWhatIsLeft()
    {
        var buffer = new RingBuffer<int>(3);

        foreach (var item in Enumerable.Range(1, 10))
        {
            buffer.Add(item);
        }

        Assert.Equal([8, 9, 10], buffer.Since(0));
    }

    [Fact]
    public void RingBuffer_MarkAheadOfTheBuffer_SeesNothing()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);

        Assert.Empty(buffer.Since(7));
    }
}
