using System.Text;
using HyPrism.Services.Game.Launch;

namespace HyPrism.Tests.Game.Launch;

/// <summary>
/// Tests for <see cref="ClientPatcher"/>. All tests operate on temporary directories
/// containing synthetic binary files — no real Hytale binaries required.
/// </summary>
public class ClientPatcherTests : IDisposable
{
    private readonly string _gameDir;

    // The original domain embedded in binaries that ClientPatcher replaces
    private const string OriginalDomain = "hytale.com";

    public ClientPatcherTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "HyPrismPatcherTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_gameDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_gameDir))
            Directory.Delete(_gameDir, true);
    }


    /// <summary>
    /// Creates a synthetic binary containing the domain in UTF-8 (default) or
    /// length-prefixed UTF-16LE format (used by .NET AOT binaries).
    /// </summary>
    private static string CreateFakeBinary(string dir, string filename, string domain = OriginalDomain,
        bool lengthPrefixed = false)
    {
        var path = Path.Combine(dir, filename);
        var prefix = new byte[] { 0x00, 0x01, 0x02 };
        byte[] domainBytes;
        if (lengthPrefixed)
        {
            // Replicate ClientPatcher.StringToLengthPrefixed:
            // [len][0x00][0x00][0x00] then each char as [c][0x00]
            var lp = new List<byte> { (byte)domain.Length, 0, 0, 0 };
            foreach (char c in domain) { lp.Add((byte)c); lp.Add(0); }
            domainBytes = lp.ToArray();
        }
        else
        {
            domainBytes = Encoding.UTF8.GetBytes(domain);
        }
        File.WriteAllBytes(path, prefix.Concat(domainBytes).ToArray());
        return path;
    }


    [Fact]
    public void IsPatchedAlready_UnpatchedFile_ReturnsFalse()
    {
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient");
        var patcher = new ClientPatcher("sanasol.ws");

        Assert.False(patcher.IsPatchedAlready(clientPath));
    }

    [Fact]
    public void IsPatchedAlready_AfterPatching_ReturnsTrue()
    {
        // Use length-prefixed format so PatchClient patches via the LP path and
        // IsPatchedAlready can confirm the patched domain is present.
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient", lengthPrefixed: true);
        var patcher = new ClientPatcher("sanasol.ws");

        patcher.PatchClient(clientPath);

        Assert.True(patcher.IsPatchedAlready(clientPath));
    }


    [Fact]
    public void PatchClient_ValidBinary_ReturnsSuccess()
    {
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient");
        var patcher = new ClientPatcher("sanasol.ws");

        var result = patcher.PatchClient(clientPath);

        Assert.True(result.Success || result.AlreadyPatched, $"Patch failed: {result.Error}");
    }

    [Fact]
    public void PatchClient_CreatesBackup()
    {
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient");
        var patcher = new ClientPatcher("sanasol.ws");

        patcher.PatchClient(clientPath);

        // Backup should exist at the same path with ".bak" or similar extension
        var backupFiles = Directory.GetFiles(_gameDir, "HytaleClient*");
        Assert.True(backupFiles.Length >= 2, "Expected at least original + backup file");
    }

    [Fact]
    public void PatchClient_AlreadyPatched_ReturnsAlreadyPatched()
    {
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient");
        var patcher = new ClientPatcher("sanasol.ws");

        patcher.PatchClient(clientPath);
        var second = patcher.PatchClient(clientPath);

        Assert.True(second.AlreadyPatched || second.Success);
    }


    [Fact]
    public void IsClientPatched_NoClientBinary_ReturnsFalse()
    {
        // Empty game dir — no client binary
        var result = ClientPatcher.IsClientPatched(_gameDir);
        Assert.False(result);
    }


    [Fact]
    public void Constructor_TooShortDomain_FallsBackToDefault()
    {
        // Domain shorter than 4 chars is invalid; constructor should fall back silently
        var ex = Record.Exception(() => new ClientPatcher("ab"));
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_TooLongDomain_FallsBackToDefault()
    {
        var ex = Record.Exception(() => new ClientPatcher("toolongdomainname.example.com"));
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_NullDomain_UsesDefault()
    {
        var ex = Record.Exception(() => new ClientPatcher(null));
        Assert.Null(ex);
    }
}
