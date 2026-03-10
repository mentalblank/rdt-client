namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetEnumerableTaskExtensions
{
    public static async IAsyncEnumerable<T> WithConcurrencyAsync<T>
    (
        this IEnumerable<Task<T>> tasks,
        Int32 concurrency
    )
    {
        if (concurrency < 1)
            throw new ArgumentException("concurrency must be greater than zero.");

        var runningTasks = new HashSet<Task<T>>();
        foreach (var task in tasks)
        {
            runningTasks.Add(task);
            if (runningTasks.Count < concurrency) continue;
            var completedTask = await Task.WhenAny(runningTasks).ConfigureAwait(false);
            runningTasks.Remove(completedTask);
            yield return await completedTask.ConfigureAwait(false);
        }

        while (runningTasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(runningTasks).ConfigureAwait(false);
            runningTasks.Remove(completedTask);
            yield return await completedTask.ConfigureAwait(false);
        }
    }
}
