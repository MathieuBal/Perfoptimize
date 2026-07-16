using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Tests;

public class BuiltInProfilesTests
{
    private static IReadOnlyList<string> EmbeddedProfileResources()
    {
        var assembly = typeof(GameProfileRepository).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Games.Profiles.", StringComparison.Ordinal) &&
                        n.EndsWith(".json", StringComparison.Ordinal))
            .ToList();
    }

    [Fact]
    public void Every_embedded_profile_parses_and_validates()
    {
        var resources = EmbeddedProfileResources();
        Assert.True(resources.Count >= 9, $"Expected the shipped profiles, found {resources.Count}.");

        // LoadBuiltIn silently drops any profile that fails to parse or validate,
        // so if a profile is malformed the loaded count won't match the file count.
        var loaded = GameProfileRepository.LoadBuiltIn();
        Assert.Equal(resources.Count, loaded.Count);

        foreach (var profile in loaded)
            Assert.Empty(GameProfileValidator.Validate(profile));
    }

    [Fact]
    public void Profile_names_are_unique()
    {
        var names = GameProfileRepository.LoadBuiltIn().Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_profile_declares_at_least_one_executable()
    {
        foreach (var profile in GameProfileRepository.LoadBuiltIn())
            Assert.NotEmpty(profile.Executables);
    }
}
