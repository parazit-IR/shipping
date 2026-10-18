using MPCore.Persistence.Abstractions;

namespace Shipping.Modules.Sample.Application.Ports;

/// <summary>
/// The Sample module's unit of work: a Sample handler commits its changes through this port, and only Sample's
/// context implements it. There is deliberately no host-wide <see cref="IUnitOfWork"/>: each module commits its
/// own schema, and a change spanning two modules is not atomic (ADR-002).
/// </summary>
public interface ISampleUnitOfWork : IUnitOfWork;
