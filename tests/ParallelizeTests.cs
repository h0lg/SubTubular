using SubTubular.Extensions;

namespace Tests;

[TestClass]
public class ParallelizeTests
{
    [TestMethod]
    public async Task YieldsEverythingFromAllProducers()
    {
        // Arrange
        var producer1 = DelayedRelease(Enumerable.Range(0, 10), TimeSpan.FromMilliseconds(1));
        var producer2 = DelayedRelease(Enumerable.Range(10, 10), TimeSpan.FromMilliseconds(1));
        var producer3 = DelayedRelease(Enumerable.Range(20, 10), TimeSpan.FromMilliseconds(1));
        var producers = new List<IAsyncEnumerable<int>> { producer1, producer2, producer3 };
        var parallelized = producers.Parallelize(CancellationToken.None);

        // Act
        var products = await ReadAllAsync(parallelized, CancellationToken.None);

        // Assert
        CollectionAssert.AreEqual(Enumerable.Range(0, 30).ToList(), products.Order().ToList());
    }

    // Parallelize method should handle cancellation gracefully
    // Products are returned before production completes
    [TestMethod]
    public async Task YieldsImmediatelyAndHandlesCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var producer1 = DelayedRelease(Enumerable.Range(1, 5), TimeSpan.FromSeconds(1));
        var producer2 = DelayedRelease(Enumerable.Range(6, 5), TimeSpan.FromSeconds(1));
        var producers = new List<IAsyncEnumerable<int>> { producer1, producer2 };
        cts.CancelAfter(TimeSpan.FromSeconds(2.5)); // yield after first 2 products from each producer

        List<int> products = new();

        // Act
        try
        {
            await foreach (var product in producers.Parallelize(cts.Token)) products.Add(product);
            Assert.Fail("Task did not throw OperationCanceledException."); // if we complete
        }
        catch (OperationCanceledException)
        {
            // assert we received the first two products from each producer, no matter in which order
            CollectionAssert.AreEqual(
                Enumerable.Range(1, 2).Concat(Enumerable.Range(6, 2)).Order().ToList(),
                products.Order().ToList());
        }
    }

    // Products are returned in the order they're produced, no matter from which producer
    [TestMethod]
    public async Task YieldsInOrderOfProduction()
    {
        // Arrange
        var producer1 = DelayedRelease(Enumerable.Range(11, 5), TimeSpan.FromMilliseconds(600));
        var producer2 = DelayedRelease(Enumerable.Range(6, 5), TimeSpan.FromMilliseconds(100));
        var producer3 = DelayedRelease(Enumerable.Range(1, 5), TimeSpan.FromMilliseconds(1));
        var producers = new List<IAsyncEnumerable<int>> { producer1, producer2, producer3 };
        var parallelized = producers.Parallelize(CancellationToken.None);

        // Act
        var products = await ReadAllAsync(parallelized, CancellationToken.None);

        // Assert
        CollectionAssert.AreEqual(Enumerable.Range(1, 15).ToList(), products);
    }

    private async Task<List<T>> ReadAllAsync<T>(IAsyncEnumerable<T> asyncEnumerable, CancellationToken cancellation)
    {
        List<T> products = new();
        await foreach (var product in asyncEnumerable.WithCancellation(cancellation)) products.Add(product);
        return products;
    }

    // Helper method to generate async enumerable with delay
    private async IAsyncEnumerable<T> DelayedRelease<T>(IEnumerable<T> enumerable, TimeSpan delay)
    {
        foreach (var item in enumerable)
        {
            await Task.Delay(delay);
            yield return item;
        }
    }
}
