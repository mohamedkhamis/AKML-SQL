using System.Collections.Generic;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Theme;

/// <summary>
/// Spec 021 (web edition) -- M2 task T035. Covers <see cref="ThemeService"/> with an
/// in-memory IndexedDB adapter and a recording-only <see cref="IThemeApplier"/>: the
/// service is verified to (a) start at System default, (b) persist + restore the
/// override across instances, and (c) call the applier when the mode changes.
/// </summary>
public sealed class ThemeServiceTests
{
    private sealed class RecordingApplier : IThemeApplier
    {
        public List<ThemeMode> Applied { get; } = new();
        public Task ApplyAsync(ThemeMode mode)
        {
            Applied.Add(mode);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Default_mode_on_a_fresh_store_is_System()
    {
        var store = new InMemoryIndexedDbAdapter();
        var applier = new RecordingApplier();
        var service = new ThemeService(store, applier);

        await service.InitializeAsync();

        Assert.Equal(ThemeMode.System, service.Current);
        Assert.Contains(ThemeMode.System, applier.Applied);
    }

    [Fact]
    public async Task SetAsync_updates_Current_and_calls_the_applier()
    {
        var store = new InMemoryIndexedDbAdapter();
        var applier = new RecordingApplier();
        var service = new ThemeService(store, applier);
        await service.InitializeAsync();

        await service.SetAsync(ThemeMode.Dark);

        Assert.Equal(ThemeMode.Dark, service.Current);
        Assert.Contains(ThemeMode.Dark, applier.Applied);
    }

    [Fact]
    public async Task SetAsync_persists_the_choice_so_a_fresh_instance_restores_it()
    {
        var store = new InMemoryIndexedDbAdapter();
        var firstApplier = new RecordingApplier();
        var first = new ThemeService(store, firstApplier);
        await first.InitializeAsync();
        await first.SetAsync(ThemeMode.HighContrast);

        // Construct a second service over the SAME store -- it should restore HighContrast.
        var secondApplier = new RecordingApplier();
        var second = new ThemeService(store, secondApplier);
        await second.InitializeAsync();

        Assert.Equal(ThemeMode.HighContrast, second.Current);
        Assert.Contains(ThemeMode.HighContrast, secondApplier.Applied);
    }

    [Fact]
    public async Task SetAsync_no_op_when_mode_is_unchanged()
    {
        var store = new InMemoryIndexedDbAdapter();
        var applier = new RecordingApplier();
        var service = new ThemeService(store, applier);
        await service.InitializeAsync();
        applier.Applied.Clear();

        // System is the current mode; setting it again should be a no-op.
        await service.SetAsync(ThemeMode.System);
        Assert.Empty(applier.Applied);
    }

    [Fact]
    public async Task Changed_event_fires_when_mode_changes()
    {
        var store = new InMemoryIndexedDbAdapter();
        var applier = new RecordingApplier();
        var service = new ThemeService(store, applier);
        await service.InitializeAsync();

        var fired = 0;
        service.Changed += () => fired++;

        await service.SetAsync(ThemeMode.Light);
        await service.SetAsync(ThemeMode.Light); // no-op
        await service.SetAsync(ThemeMode.Dark);

        Assert.Equal(2, fired);
    }
}
