using EDNexus.Ebs.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EDNexus.Ebs.Tests;

public sealed class DataProtectionKeyRingMoveTests : IDisposable
{
    private readonly TempEbsDataDirectory _data = new();

    public void Dispose() => _data.Dispose();

    private string SeparateKeysPath => Path.Combine(_data.Path, "separate-keys");

    [Fact]
    public void Grants_encrypted_before_the_move_still_decrypt_after_it()
    {
        // An existing deployment: key ring in the default {DataDirectory}/keys.
        var issued = _data.CreateTokenStore().IssueToken("chan-1", "CMDR", "twitch-access", "twitch-refresh", DateTimeOffset.UtcNow.AddHours(4));

        var moved = DataProtectionKeyRingMove.MoveIfNeeded(_data.KeysPath, SeparateKeysPath, NullLogger.Instance);

        // Restarted against the separate directory: nobody is logged out.
        Assert.True(moved > 0);
        Assert.True(_data.CreateTokenStore(keysPath: SeparateKeysPath).TryGetByToken(issued.Token, out var found));
        Assert.Equal("twitch-access", found.TwitchAccessToken);

        // And the database's volume no longer holds the keys that decrypt it.
        Assert.Empty(Directory.GetFiles(_data.KeysPath, "*.xml"));
    }

    [Fact]
    public void Nothing_happens_when_the_configured_directory_is_the_default()
    {
        _data.CreateTokenStore().IssueToken("chan-1", "CMDR", "a", "r", DateTimeOffset.UtcNow.AddHours(4));

        Assert.Equal(0, DataProtectionKeyRingMove.MoveIfNeeded(_data.KeysPath, _data.KeysPath + Path.DirectorySeparatorChar, NullLogger.Instance));
        Assert.NotEmpty(Directory.GetFiles(_data.KeysPath, "*.xml"));
    }

    [Fact]
    public void Nothing_happens_on_a_fresh_install()
    {
        Assert.Equal(0, DataProtectionKeyRingMove.MoveIfNeeded(_data.KeysPath, SeparateKeysPath, NullLogger.Instance));
    }

    [Fact]
    public void Two_rings_are_both_left_alone()
    {
        _data.CreateTokenStore().IssueToken("chan-1", "CMDR", "a", "r", DateTimeOffset.UtcNow.AddHours(4));
        _data.CreateTokenStore(keysPath: SeparateKeysPath).IssueToken("chan-2", "CMDR", "a", "r", DateTimeOffset.UtcNow.AddHours(4));
        var before = Directory.GetFiles(_data.KeysPath, "*.xml").Length;

        // Which ring is current is not something to guess: overwriting either could orphan grants.
        Assert.Equal(0, DataProtectionKeyRingMove.MoveIfNeeded(_data.KeysPath, SeparateKeysPath, NullLogger.Instance));
        Assert.Equal(before, Directory.GetFiles(_data.KeysPath, "*.xml").Length);
    }
}
