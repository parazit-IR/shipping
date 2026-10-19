using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MPCore.Domain.Events;

namespace Shipping.Modules.Sample.Infrastructure.Persistence;

/// <summary>
/// Sample is ready when its own context can open a connection to the Shipping database. EF Core opens one and
/// closes it again: nothing of the module is read or written, and nothing is created or migrated.
/// </summary>
/// <remarks>
/// <para>
/// The check stops waiting at its timeout and answers, but the attempt does not stop: opening a connection can
/// ignore cancellation (with the postgres container stopped it took 10-12 s; Npgsql's own limit is 15 s). The
/// attempt therefore runs on a context of its own, never the request's, and disposes it only when it ends.
/// Disposing a context while it is still connecting throws, which the request's scope would have done.
/// </para>
/// <para>
/// A running attempt holds a slot of the connection pool that business traffic uses. <see cref="Attempt"/> keeps
/// at most one for this module per host: a check that finds one still running waits for it instead of starting another.
/// After the database comes back, readiness can therefore lag by what is left of one attempt.
/// </para>
/// <para>
/// The options and the event sink are used after the check's scope ends. That is safe: the options are immutable
/// and this context never saves.
/// </para>
/// </remarks>
internal sealed class SampleDatabaseHealthCheck(
    SampleDatabaseHealthCheck.Attempt attempt,
    DbContextOptions<SampleDbContext> options,
    TimeProvider timeProvider,
    IAggregateEventSink eventSink) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await attempt.JoinOrStart(CanConnectAsync).WaitAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus);
    }

    private async Task<bool> CanConnectAsync()
    {
        await using var dbContext = new SampleDbContext(options, timeProvider, eventSink);
        return await dbContext.Database.CanConnectAsync(CancellationToken.None);
    }

    /// <summary>This module's connection attempt that is still running in this host, if any. A singleton.</summary>
    internal sealed class Attempt
    {
        private readonly Lock _sync = new();
        private Task<bool>? _running;

        public Task<bool> JoinOrStart(Func<Task<bool>> start)
        {
            lock (_sync)
            {
                if (_running is not { IsCompleted: false })
                {
                    _running = start();
                }

                return _running;
            }
        }
    }
}
