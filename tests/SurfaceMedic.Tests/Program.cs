using SurfaceMedic.Core.Models;
using SurfaceMedic.Core.Services;

var tests = new (string Name, Action Run)[]
{
    ("winget search table", TestWingetSearchTable),
    ("winget update table", TestWingetUpdateTable),
    ("winget malformed output", TestWingetMalformedOutput),
    ("battery health boundaries", TestBatteryHealthBoundaries),
    ("storage health boundaries", TestStorageHealthBoundaries),
    ("thermal health priority", TestThermalHealthPriority),
    ("disk health telemetry", TestDiskHealthTelemetry),
    ("power health modes", TestPowerHealthModes)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
if (failures == 0 && args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        await TestLiveReadOnlyAdaptersAsync();
        Console.WriteLine("PASS live read-only adapters");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL live read-only adapters: {exception.Message}");
    }
}

return failures == 0 ? 0 : 1;

static void TestWingetSearchTable()
{
    var schema = new[]
    {
        ("Name", 30),
        ("Id", 34),
        ("Version", 14),
        ("Match", 24),
        ("Source", 0)
    };
    var output = string.Join(Environment.NewLine,
        "The msstore source requires that you view the following agreements before using.",
        BuildRow(schema, "Name", "Id", "Version", "Match", "Source"),
        new string('-', 116),
        BuildRow(schema, "Microsoft PowerToys", "Microsoft.PowerToys", "0.92.1", "Tag: utilities", "winget"),
        BuildRow(schema, "A deliberately long name\u2026", "Vendor.LongPackage", "2026.7", "Moniker: long", "winget"),
        "2 packages found.");

    var packages = WingetTableParser.Parse(output);
    Equal(2, packages.Count);
    Equal("Microsoft.PowerToys", packages[0].Id);
    Equal("Tag: utilities", packages[0].Match);
    Equal("A deliberately long name\u2026", packages[1].Name);
    Equal("winget", packages[1].Source);
}

static void TestWingetUpdateTable()
{
    var schema = new[]
    {
        ("Name", 28),
        ("Id", 34),
        ("Version", 14),
        ("Available", 14),
        ("Source", 0)
    };
    var output = string.Join(Environment.NewLine,
        BuildRow(schema, "Name", "Id", "Version", "Available", "Source"),
        new string('-', 98),
        BuildRow(schema, "HWiNFO", "REALiX.HWiNFO", "8.28", "8.30", "winget"),
        "1 upgrades available.",
        "The following packages have an upgrade available, but require explicit targeting:");

    var packages = WingetTableParser.Parse(output);
    Equal(1, packages.Count);
    Equal("8.28", packages[0].Version);
    Equal("8.30", packages[0].Available);
    Equal(string.Empty, packages[0].Match);
}

static void TestWingetMalformedOutput()
{
    Equal(0, WingetTableParser.Parse("No package found matching input criteria.").Count);
    Equal(0, WingetTableParser.Parse((IEnumerable<string>?)null).Count);
}

static void TestBatteryHealthBoundaries()
{
    Equal(HealthState.Unavailable, HealthAssessor.AssessBattery(false, null, null).State);
    Equal(HealthState.Healthy, HealthAssessor.AssessBattery(true, 50_000, 46_000).State);
    Equal(HealthState.Advisory, HealthAssessor.AssessBattery(true, 50_000, 44_000).State);
    Equal(HealthState.Warning, HealthAssessor.AssessBattery(true, 50_000, 40_000).State);
    Equal(HealthState.Critical, HealthAssessor.AssessBattery(true, 50_000, 32_500).State);
}

static void TestStorageHealthBoundaries()
{
    Equal(HealthState.Unavailable, HealthAssessor.AssessStorage(null, null).State);
    Equal(HealthState.Healthy, HealthAssessor.AssessStorage(100, 21).State);
    Equal(HealthState.Advisory, HealthAssessor.AssessStorage(100, 20).State);
    Equal(HealthState.Warning, HealthAssessor.AssessStorage(100, 10).State);
    Equal(HealthState.Critical, HealthAssessor.AssessStorage(100, 5).State);
}

static void TestThermalHealthPriority()
{
    Equal(HealthState.Healthy, HealthAssessor.AssessThermal(0, 0, 0).State);
    Equal(HealthState.Advisory, HealthAssessor.AssessThermal(0, 2, 0).State);
    Equal(HealthState.Warning, HealthAssessor.AssessThermal(1, 2, 0).State);
    Equal(HealthState.Critical, HealthAssessor.AssessThermal(0, 0, 1).State);
}

static void TestDiskHealthTelemetry()
{
    Equal(HealthState.Healthy, HealthAssessor.AssessDisk("Healthy", 10, 42).State);
    Equal(HealthState.Advisory, HealthAssessor.AssessDisk("Healthy", 50, 42).State);
    Equal(HealthState.Warning, HealthAssessor.AssessDisk("Healthy", 10, 60).State);
    Equal(HealthState.Critical, HealthAssessor.AssessDisk("Warning", 10, 42).State);
}

static void TestPowerHealthModes()
{
    Equal(HealthState.Unavailable, HealthAssessor.AssessPower(null, null).State);
    Equal(HealthState.Healthy, HealthAssessor.AssessPower(99, 99).State);
    Equal(HealthState.Advisory, HealthAssessor.AssessPower(99, 100).State);
    Equal(HealthState.Advisory, HealthAssessor.AssessPower(100, 100).State);
}

static async Task TestLiveReadOnlyAdaptersAsync()
{
    ISurfaceMedicService service = new SurfaceMedicService();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var dashboard = await service.GetDashboardAsync(cancellationToken: timeout.Token);
    if (dashboard.CollectedAt == default)
    {
        throw new InvalidOperationException("Dashboard collection did not set its timestamp.");
    }

    var power = await service.GetPowerStatusAsync(cancellationToken: timeout.Token);
    if (power.CollectedAt == default)
    {
        throw new InvalidOperationException("Power collection did not set its timestamp.");
    }

    _ = await service.ScanThermalEventsAsync(7, cancellationToken: timeout.Token);
}

static string BuildRow((string Name, int Width)[] schema, params string[] values)
{
    var fields = new List<string>();
    for (var index = 0; index < schema.Length; index++)
    {
        var value = values[index];
        fields.Add(schema[index].Width > 0 ? value.PadRight(schema[index].Width) : value);
    }

    return string.Concat(fields);
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }
}
