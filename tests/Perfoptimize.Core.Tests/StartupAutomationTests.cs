using Perfoptimize.Core.Startup;

namespace Perfoptimize.Core.Tests;

public class StartupIdentityTests
{
    [Fact]
    public void Compute_is_deterministic_and_stable()
    {
        var a = StartupIdentity.Compute(StartupLocation.RegistryRunUser, "Updater", @"C:\u.exe");
        var b = StartupIdentity.Compute(StartupLocation.RegistryRunUser, "Updater", @"C:\u.exe");
        Assert.Equal(a, b);
        Assert.Equal(16, a.Length); // 8 bytes as hex
    }

    [Fact]
    public void Compute_differs_when_anything_that_names_the_entry_differs()
    {
        var baseId = StartupIdentity.Compute(StartupLocation.RegistryRunUser, "Updater", @"C:\u.exe");
        Assert.NotEqual(baseId, StartupIdentity.Compute(StartupLocation.RegistryRunMachine, "Updater", @"C:\u.exe"));
        Assert.NotEqual(baseId, StartupIdentity.Compute(StartupLocation.RegistryRunUser, "Other", @"C:\u.exe"));
        Assert.NotEqual(baseId, StartupIdentity.Compute(StartupLocation.RegistryRunUser, "Updater", @"C:\v.exe"));
    }

    [Theory]
    [InlineData(StartupLocation.RegistryRunUser, StartupDisableMethod.Registry)]
    [InlineData(StartupLocation.RegistryRunMachine, StartupDisableMethod.Registry)]
    [InlineData(StartupLocation.StartupFolder, StartupDisableMethod.StartupFile)]
    [InlineData(StartupLocation.ScheduledTask, StartupDisableMethod.ScheduledTask)]
    public void MethodFor_maps_each_location(StartupLocation location, StartupDisableMethod expected)
    {
        Assert.Equal(expected, StartupIdentity.MethodFor(location));
    }

    [Theory]
    [InlineData(StartupLocation.RegistryRunUser, false)]
    [InlineData(StartupLocation.StartupFolder, false)]
    [InlineData(StartupLocation.RegistryRunMachine, true)]
    [InlineData(StartupLocation.ScheduledTask, true)]
    public void NeedsElevation_flags_the_protected_locations(StartupLocation location, bool expected)
    {
        Assert.Equal(expected, StartupIdentity.NeedsElevation(location));
    }
}

public class StartupPlannerTests
{
    [Fact]
    public void Plan_describes_a_reversible_registry_change()
    {
        var entry = new StartupEntry
        {
            Name = "ShadyUpdater",
            Command = @"C:\Temp\shady.exe",
            Location = StartupLocation.RegistryRunUser,
        };

        var plan = StartupPlanner.Plan(entry);

        Assert.Equal(entry.Id, plan.Id);
        Assert.Equal(StartupDisableMethod.Registry, plan.Method);
        Assert.True(plan.Reversible);
        Assert.False(plan.NeedsElevation);
        Assert.Contains("registre", plan.Action);
        Assert.NotEqual("", plan.Reversal);
    }

    [Fact]
    public void Plan_flags_elevation_for_a_scheduled_task()
    {
        var entry = new StartupEntry
        {
            Name = @"\Vendor\Update",
            Command = @"C:\vendor\update.exe",
            Location = StartupLocation.ScheduledTask,
        };

        var plan = StartupPlanner.Plan(entry);

        Assert.Equal(StartupDisableMethod.ScheduledTask, plan.Method);
        Assert.True(plan.NeedsElevation);
        Assert.Contains("schtasks", plan.Action);
    }
}

public class StartupBackupStoreTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "perfopt-startup-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_find_and_remove_round_trip()
    {
        var dir = TempDir();
        try
        {
            var store = new StartupBackupStore(dir);
            var backup = new StartupBackup
            {
                Id = "abc123",
                Name = "Updater",
                Command = @"C:\u.exe",
                Location = StartupLocation.RegistryRunUser,
                Method = StartupDisableMethod.Registry,
                DisabledAt = DateTimeOffset.Now,
                RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
                RegistryValueName = "Updater",
                RegistryValueKind = "String",
            };

            Assert.Null(store.Find("abc123"));
            store.Save(backup);

            var found = store.Find("abc123");
            Assert.NotNull(found);
            Assert.Equal("Updater", found!.Name);
            Assert.Equal(StartupDisableMethod.Registry, found.Method);
            Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", found.RegistryKeyPath);

            store.Remove("abc123");
            Assert.Null(store.Find("abc123"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void List_returns_most_recent_first()
    {
        var dir = TempDir();
        try
        {
            var store = new StartupBackupStore(dir);
            store.Save(new StartupBackup { Id = "old", Name = "Old", DisabledAt = DateTimeOffset.Now.AddHours(-1) });
            store.Save(new StartupBackup { Id = "new", Name = "New", DisabledAt = DateTimeOffset.Now });

            var list = store.List();
            Assert.Equal(2, list.Count);
            Assert.Equal("New", list[0].Name);
            Assert.Equal("Old", list[1].Name);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_directory_yields_an_empty_list()
    {
        var store = new StartupBackupStore(TempDir()); // never created
        Assert.Empty(store.List());
    }

    [Fact]
    public void A_crafted_id_cannot_escape_the_store_directory()
    {
        var dir = TempDir();
        try
        {
            var store = new StartupBackupStore(dir);
            store.Save(new StartupBackup { Id = "../../evil", Name = "Evil", DisabledAt = DateTimeOffset.Now });
            // The sanitised file must live inside the store directory.
            var files = Directory.GetFiles(dir, "*.json");
            Assert.Single(files);
            Assert.StartsWith(Path.GetFullPath(dir), Path.GetFullPath(files[0]));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
