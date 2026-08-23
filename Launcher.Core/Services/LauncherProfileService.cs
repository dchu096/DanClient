using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class LauncherProfileService : ILauncherProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<LauncherProfile>> LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        var store = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
        return store.Profiles;
    }

    public async Task<LauncherProfile> GetSelectedProfileAsync(CancellationToken cancellationToken = default)
    {
        var store = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
        return store.Profiles.FirstOrDefault(profile => profile.Id == store.SelectedProfileId)
               ?? store.Profiles.First();
    }

    public async Task SaveProfileAsync(
        LauncherProfile profile,
        bool select,
        CancellationToken cancellationToken = default)
    {
        var store = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
        var profiles = store.Profiles.ToList();
        var index = profiles.FindIndex(existing => existing.Id == profile.Id);
        if (index >= 0)
        {
            profiles[index] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        var next = new ProfileStore(
            select ? profile.Id : store.SelectedProfileId,
            profiles);
        await SaveStoreAsync(next, cancellationToken).ConfigureAwait(false);
    }

    public async Task SelectProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var store = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
        if (store.Profiles.All(profile => profile.Id != profileId))
        {
            throw new InvalidOperationException($"Profile {profileId} does not exist.");
        }

        await SaveStoreAsync(store with { SelectedProfileId = profileId }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LauncherProfile> DeleteProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var store = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
        if (store.Profiles.Count <= 1)
        {
            throw new InvalidOperationException("Keep at least one profile.");
        }

        var profiles = store.Profiles
            .Where(profile => profile.Id != profileId)
            .ToList();
        if (profiles.Count == store.Profiles.Count)
        {
            throw new InvalidOperationException($"Profile {profileId} does not exist.");
        }

        var selected = profiles.FirstOrDefault(profile => profile.Id == store.SelectedProfileId)
                       ?? profiles.First();
        await SaveStoreAsync(new ProfileStore(selected.Id, profiles), cancellationToken).ConfigureAwait(false);
        return selected;
    }

    private static async Task<ProfileStore> LoadStoreAsync(CancellationToken cancellationToken)
    {
        AppPaths.Ensure();
        if (!File.Exists(AppPaths.ProfilesFile))
        {
            var defaultStore = CreateDefaultStore();
            await SaveStoreAsync(defaultStore, cancellationToken).ConfigureAwait(false);
            return defaultStore;
        }

        try
        {
            await using var stream = File.OpenRead(AppPaths.ProfilesFile);
            var store = await JsonSerializer.DeserializeAsync<ProfileStore>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (store is { Profiles.Count: > 0 })
            {
                EnsureProfileDirectories(store.Profiles);
                return store;
            }
        }
        catch
        {
            // Recreate the profile store below if the JSON is unreadable.
        }

        var fallback = CreateDefaultStore();
        await SaveStoreAsync(fallback, cancellationToken).ConfigureAwait(false);
        return fallback;
    }

    private static async Task SaveStoreAsync(ProfileStore store, CancellationToken cancellationToken)
    {
        AppPaths.Ensure();
        EnsureProfileDirectories(store.Profiles);
        await using var stream = File.Create(AppPaths.ProfilesFile);
        await JsonSerializer.SerializeAsync(stream, store, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static ProfileStore CreateDefaultStore()
    {
        var profile = new LauncherProfile(
            "default",
            "Survival",
            null,
            false,
            true,
            4096,
            string.Empty,
            string.Empty,
            AppPaths.GetProfileInstance("default"));
        return new ProfileStore(profile.Id, [profile]);
    }

    private static void EnsureProfileDirectories(IEnumerable<LauncherProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            Directory.CreateDirectory(profile.GameDirectory);
            Directory.CreateDirectory(profile.ModsDirectory);
        }
    }

    private sealed record ProfileStore(
        [property: JsonPropertyName("selectedProfileId")] string SelectedProfileId,
        [property: JsonPropertyName("profiles")] List<LauncherProfile> Profiles);
}
