namespace SubTubular.Extensions;
using System;
using System.Threading.Tasks;

/*TODO ideas for managing parallelism / preventing background tasks from choking system or UI thread
       Partitioner class with Task.WhenAll https://www.youtube.com/watch?v=lHuyl_WTpME
       https://stackoverflow.com/questions/33928122/how-to-correctly-queue-up-tasks-to-run-in-c-sharp
       */
public static class BigTask
{
    // The custom task scheduler for resource-intensive tasks
    private static readonly TaskFactory TaskFactory = new(TaskScheduler.Current);

    /// <summary>
    /// Creates and starts a cold (deferred) task.
    /// The task is scheduled but not executed immediately.
    /// </summary>
    /// <param name="func">The function to execute asynchronously.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public static Task CreateColdTask(Func<Task> func)
    {
        // Create a task that will not start executing until explicitly started
        var task = new Task(async () => await func());
        TaskFactory.StartNew(task.Start);
        return task;
    }

    /// <summary>
    /// Creates and starts a cold (deferred) task that returns a result.
    /// The task is scheduled but not executed immediately.
    /// </summary>
    /// <typeparam name="TResult">The type of the result produced by the task.</typeparam>
    /// <param name="func">The function to execute asynchronously.</param>
    /// <returns>A Task<TResult> representing the asynchronous operation.</returns>
    /*public static Task<TResult> CreateColdTask<TResult>(Func<Task<TResult>> func)
    {
        // Create a task that will not start executing until explicitly started
        var task = new Task<TResult>(async () => { return await func(); });
        TaskFactory.StartNew(task.Start);
        return task;
    }*/

    /// <summary>
    /// Creates and starts a hot (eager) task.
    /// The task is started immediately.
    /// </summary>
    /// <param name="func">The function to execute asynchronously.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public static Task Run(Func<Task> func)
    {
        // Create and start a hot task immediately
        return TaskFactory.StartNew(func).Unwrap();
    }

    /// <summary>
    /// Creates and starts a hot (eager) task that returns a result.
    /// The task is started immediately.
    /// </summary>
    /// <typeparam name="TResult">The type of the result produced by the task.</typeparam>
    /// <param name="func">The function to execute asynchronously.</param>
    /// <returns>A Task<TResult> representing the asynchronous operation.</returns>
    public static Task<TResult> Run<TResult>(Func<Task<TResult>> func)
    {
        // Create and start a hot task immediately
        return TaskFactory.StartNew(func).Unwrap();
    }
}
