namespace MagiEngine.Tests.Fixtures;

/// <summary>Shared access to the 4 real-data fixtures (same files as the Android app's
/// <c>app/src/test/resources/</c>) used across multiple test classes.</summary>
internal static class FixtureLoader
{
    public static readonly TheoryData<string> AllFiles = new()
    {
        "golden_state.json",
        "sample_state_v6.json",
        "blocked_covu_state.json",
        "sept2026_state.json",
    };

    public static string ReadRaw(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
