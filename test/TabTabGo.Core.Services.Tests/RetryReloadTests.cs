using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.TestKit;

namespace TabTabGo.Core.Services.Tests;

/// <summary>F3, F4: a retry reloads the entity and applies only what the patch changed.</summary>
public class RetryReloadTests : IDisposable
{
    private static Expression<Func<Widget, bool>> ScopeA => w => w.Tenant == "A";

    private readonly SqliteFixture _db = new(retrying: true);

    public RetryReloadTests()
    {
        _db.BumpVersion = true;
        _db.SeedWidget();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    private (FailOnceAfterWrite Failure, TestWidgetService Service, TestDbContext Context) Start(bool failOnce = true)
    {
        var failure = new FailOnceAfterWrite();
        if (failOnce)
        {
            _db.DefaultInterceptors.Add(failure);
        }

        var context = _db.CreateContext();
        return (failure, new TestWidgetService(new TestUnitOfWork(context)), context);
    }

    private Widget Stored()
    {
        using var fresh = _db.CreateContext();
        return fresh.Widgets.AsNoTracking().Single();
    }

    [Fact]
    public async Task UpdateChanges_Scoped_ConcurrencyTokenWithARetry_Succeeds()
    {
        var (failure, service, context) = Start();
        using var _ = context;
        var widget = (await service.GetByKey(1, cancellationToken: Ct))!;
        widget.Name = "patched";

        // the failed attempt gave the old object a new token that the database rolled back: re-saving that object matched 0 rows
        await service.UpdateChanges(1, widget, ScopeA, Ct);

        Assert.Equal(1, failure.Failures);
        Assert.Equal("patched", Stored().Name);
    }

    [Fact]
    public async Task UpdateChanges_Scoped_ConcurrencyTokenWithoutARetry_Succeeds()
    {
        var (_, service, context) = Start(failOnce: false);
        using var _ = context;
        var widget = (await service.GetByKey(1, cancellationToken: Ct))!;
        widget.Name = "patched";

        await service.UpdateChanges(1, widget, ScopeA, Ct);

        Assert.Equal("patched", Stored().Name);
    }

    [Fact]
    public async Task UpdateChanges_Scoped_RetryKeepsWhatAConcurrentWriterCommittedInBetween()
    {
        _db.OnRetry = () =>
        {
            using var command = _db.Connection.CreateCommand();
            command.CommandText = "UPDATE Widgets SET Note = 'concurrent' WHERE Id = 1";
            command.ExecuteNonQuery();
        };
        var (failure, service, context) = Start();
        using var _ = context;
        var widget = (await service.GetByKey(1, cancellationToken: Ct))!;
        widget.Name = "patched";

        await service.UpdateChanges(1, widget, ScopeA, Ct);

        Assert.Equal(1, failure.Failures);
        var stored = Stored();
        Assert.Equal("patched", stored.Name);
        Assert.Equal("concurrent", stored.Note); // the retry used to write every column of the old object back
    }

    [Fact]
    public async Task UpdateChanges_Scoped_RetryKeepsAShadowColumn()
    {
        var (_, service, context) = Start();
        using var _ = context;
        var widget = (await service.GetByKey(1, cancellationToken: Ct))!;
        widget.Name = "patched";

        await service.UpdateChanges(1, widget, ScopeA, Ct);

        using var fresh = _db.CreateContext();
        var stored = fresh.Widgets.Single();
        Assert.Equal("EU", fresh.Entry(stored).Property("Region").CurrentValue);
    }

    [Fact]
    public async Task UpdateChanges_Scoped_RetryOfAChangeThatLeavesTheScope_IsStillRejected()
    {
        var (_, service, context) = Start();
        using var _ = context;
        var widget = (await service.GetByKey(1, cancellationToken: Ct))!;
        widget.Tenant = "B";
        widget.Name = "escaped";

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.UpdateChanges(1, widget, ScopeA, Ct));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.HttpStatusCode);
        Assert.Equal("w1", Stored().Name);
        Assert.Equal("A", Stored().Tenant);
    }

    [Fact]
    public async Task UpdateChanges_Scoped_RetryOfARowThatLeftTheScopeInBetween_Is404()
    {
        _db.OnRetry = () =>
        {
            using var command = _db.Connection.CreateCommand();
            command.CommandText = "UPDATE Widgets SET Tenant = 'B' WHERE Id = 1";
            command.ExecuteNonQuery();
        };
        var (_, service, context) = Start();
        using var _ = context;
        var widget = (await service.GetByKey(1, cancellationToken: Ct))!;
        widget.Name = "patched";

        await Assert.ThrowsAsync<ApiException>(() => service.UpdateChanges(1, widget, ScopeA, Ct));

        Assert.Equal("w1", Stored().Name);
    }
}
