using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MPCore.Domain.Events;
using MPCore.Domain.Model;
using MPCore.Persistence.Abstractions;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;

namespace Shipping.IntegrationTests;

// Two probe modules, Alpha and Beta, for tests only. Sample has no entities, and showing that a write lands in
// its module's schema, and that two modules' units of work do not interfere, needs tables. Each probe follows
// the convention of a real module (ADR-002): its own MP Core runtime context and unit-of-work port, its own
// schema by default, its own migrations and migration history in that schema, and the same registration as
// Sample's. No business meaning.

/// <summary>An aggregate with MP Core's timestamps and events, so a save shows MP Core's behaviour.</summary>
public sealed class ProbeRecord : AggregateRoot<Guid>
{
    private ProbeRecord()
    {
    }

    public ProbeRecord(Guid id, string name)
        : base(id)
    {
        Name = name;
        Raise(new ProbeRecorded(id));
    }

    public string Name { get; private set; } = string.Empty;
}

public sealed record ProbeRecorded(Guid Id) : IDomainEvent;

public interface IAlphaProbeUnitOfWork : IUnitOfWork;

public interface IBetaProbeUnitOfWork : IUnitOfWork;

public abstract class ProbeDbContext(DbContextOptions options, TimeProvider timeProvider, IAggregateEventSink eventSink, string schema)
    : MPCoreDbContext(options, timeProvider, eventSink)
{
    public const string Table = "records";

    public string Schema { get; } = schema;

    public DbSet<ProbeRecord> Records => Set<ProbeRecord>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<ProbeRecord>(record =>
        {
            record.ToTable(Table);
            record.HasKey(static row => row.Id);
            record.Property(static row => row.Id).ValueGeneratedNever();
            record.Ignore(static row => row.DomainEvents);
            record.Ignore(static row => row.IntegrationEvents);
        });
    }

    /// <summary>The probe's migration: its table in its schema.</summary>
    internal static void CreateTable(MigrationBuilder migrationBuilder, string schema)
    {
        migrationBuilder.EnsureSchema(schema);
        migrationBuilder.CreateTable(
            name: Table,
            schema: schema,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ModifiedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_records", static row => row.Id));
    }

    internal static TContext Create<TContext>(string connectionString, Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        PostgreSqlDbContextOptions.Apply(options, connectionString);
        return create(options.Options);
    }
}

public sealed class AlphaProbeDbContext(DbContextOptions<AlphaProbeDbContext> options, TimeProvider timeProvider, IAggregateEventSink eventSink)
    : ProbeDbContext(options, timeProvider, eventSink, SchemaName), IAlphaProbeUnitOfWork
{
    public const string SchemaName = "probe_alpha";

    public static AlphaProbeDbContext Create(string connectionString) =>
        Create<AlphaProbeDbContext>(connectionString, static options => new(options, TimeProvider.System, NullAggregateEventSink.Instance));
}

public sealed class BetaProbeDbContext(DbContextOptions<BetaProbeDbContext> options, TimeProvider timeProvider, IAggregateEventSink eventSink)
    : ProbeDbContext(options, timeProvider, eventSink, SchemaName), IBetaProbeUnitOfWork
{
    public const string SchemaName = "probe_beta";

    public static BetaProbeDbContext Create(string connectionString) =>
        Create<BetaProbeDbContext>(connectionString, static options => new(options, TimeProvider.System, NullAggregateEventSink.Instance));
}

[DbContext(typeof(AlphaProbeDbContext))]
[Migration("20261015000000_AlphaProbeBaseline")]
public sealed class AlphaProbeBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => ProbeDbContext.CreateTable(migrationBuilder, AlphaProbeDbContext.SchemaName);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: ProbeDbContext.Table, schema: AlphaProbeDbContext.SchemaName);
}

[DbContext(typeof(BetaProbeDbContext))]
[Migration("20261015000000_BetaProbeBaseline")]
public sealed class BetaProbeBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => ProbeDbContext.CreateTable(migrationBuilder, BetaProbeDbContext.SchemaName);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: ProbeDbContext.Table, schema: BetaProbeDbContext.SchemaName);
}

public static class ProbeModuleRegistration
{
    /// <summary>Registers a probe module exactly as <c>AddSampleInfrastructure</c> registers Sample.</summary>
    public static IServiceCollection AddProbeModule<TContext, TUnitOfWork>(this IServiceCollection services, string connectionString)
        where TContext : DbContext, TUnitOfWork
        where TUnitOfWork : class
    {
        services.AddDbContext<TContext>(options => PostgreSqlDbContextOptions.Apply(options, connectionString));
        services.AddScoped<TUnitOfWork>(static provider => provider.GetRequiredService<TContext>());
        return services;
    }
}

/// <summary>Records what MP Core drains from the aggregates, and can be told to fail, like a real sink's write.</summary>
public sealed class RecordingEventSink : IAggregateEventSink
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public bool Fail { get; set; }

    public IReadOnlyList<IDomainEvent> DomainEvents
    {
        get { lock (_domainEvents) return _domainEvents.ToList(); }
    }

    public ValueTask CollectAsync(IReadOnlyList<IDomainEvent> domainEvents, IReadOnlyList<IIntegrationEvent> integrationEvents,
        CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("The probe event sink failed.");
        }

        lock (_domainEvents) _domainEvents.AddRange(domainEvents);
        return ValueTask.CompletedTask;
    }
}

/// <summary>A clock that always reads the same instant, so MP Core's stamping can be checked exactly.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
