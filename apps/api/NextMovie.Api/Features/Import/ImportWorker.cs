using NextMovie.Api.Domain.Import;

namespace NextMovie.Api.Features.Import;

/// <summary>
/// Drains the import queue.
/// </summary>
/// <remarks>
/// Deliberately thin: claim a job, work it, repeat, and sleep when there is
/// nothing to do. Everything worth testing lives in
/// <see cref="LetterboxdImportProcessor"/>, which can be called directly.
/// <para>
/// Runs in the API process (ADR-0007). That is fine at this size and is the thing
/// to change first if imports ever start competing with request handling —
/// because the queue is a table, moving this out is a deployment change rather
/// than a rewrite.
/// </para>
/// </remarks>
internal sealed class ImportWorker(IServiceScopeFactory scopes, ILogger<ImportWorker> logger)
    : BackgroundService
{
    /// <summary>
    /// How long to wait after finding nothing to do.
    /// </summary>
    /// <remarks>
    /// Polling puts a floor on how soon an import starts. Five seconds is
    /// invisible next to a two-minute import and keeps the query rate trivial;
    /// this is not a low-latency queue and should not become one.
    /// </remarks>
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Import worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await WorkOneJobAsync(stoppingToken))
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Never let the loop die. A failure here is usually the database
                // being briefly unavailable, and a worker that exited on the
                // first blip would leave every later import stuck on Pending with
                // nothing to say why.
                logger.LogError(exception, "Import worker pass failed; retrying");

                await Task.Delay(IdleDelay, stoppingToken);
            }
        }

        logger.LogInformation("Import worker stopped");
    }

    /// <returns>Whether there was a job to work.</returns>
    private async Task<bool> WorkOneJobAsync(CancellationToken stoppingToken)
    {
        // A scope per job: the processor depends on a scoped DbContext, and one
        // held for the lifetime of the process would accumulate every entity it
        // ever tracked.
        await using var scope = scopes.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<LetterboxdImportProcessor>();

        var job = await processor.ClaimNextJobAsync(stoppingToken);

        if (job is null)
        {
            return false;
        }

        await processor.ProcessAsync(job, stoppingToken);

        return true;
    }
}
