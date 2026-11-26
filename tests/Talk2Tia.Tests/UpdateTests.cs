using System;
using System.IO;
using Talk2Tia.Core.Configuration;
using Talk2Tia.Core.Updates;
using Xunit;

namespace Talk2Tia.Tests;

[Collection(OpennessCollection.Name)]
public sealed class UpdateTests
{
    private const string Releases = """
        [
          { "tag_name": "v0.14.0", "draft": true, "html_url": "d", "assets": [ { "name": "Talk2Tia-0.14.0-x64.msi", "browser_download_url": "u14" } ] },
          { "tag_name": "v0.13.0", "draft": false, "html_url": "p13", "assets": [
              { "name": "SHA256SUMS.txt", "browser_download_url": "s13" },
              { "name": "Talk2Tia-0.13.0-x64.msi", "browser_download_url": "https://x/v0.13.0/Talk2Tia-0.13.0-x64.msi" } ] },
          { "tag_name": "v0.12.0", "draft": false, "html_url": "p12", "assets": [ { "name": "Talk2Tia-0.12.0-x64.msi", "browser_download_url": "u12" } ] },
          { "tag_name": "v0.15.0", "draft": false, "html_url": "p15", "assets": [ { "name": "notes.txt", "browser_download_url": "n" } ] },
          { "tag_name": "nightly", "draft": false, "html_url": "n", "assets": [] }
        ]
        """;

    [Fact]
    public void Latest_release_skips_drafts_and_releases_without_msi()
    {
        var latest = UpdateService.PickLatest(Releases);
        Assert.NotNull(latest);
        Assert.Equal(new Version(0, 13, 0), latest!.Version);
        Assert.Equal("Talk2Tia-0.13.0-x64.msi", latest.MsiName);
        Assert.Equal("p13", latest.Page);
    }

    [Fact]
    public void Checksums_are_read_by_file_name()
    {
        var sums = "aa11  Talk2Tia-0.13.0-x64-portable.zip\r\nBB22  Talk2Tia-0.13.0-x64.msi\r\n";
        Assert.Equal("bb22", UpdateService.ShaFor(sums, "Talk2Tia-0.13.0-x64.msi"));
        Assert.Null(UpdateService.ShaFor(sums, "other.msi"));
    }

    [Theory]
    [InlineData("v0.13.0", "0.13.0")]
    [InlineData("0.13", "0.13.0")]
    [InlineData("1.2.3-beta.1", "1.2.3")]
    [InlineData("0.13.0+abc", "0.13.0")]
    public void Versions_are_parsed_from_tags(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateService.ParseVersion(tag));
    }

    [Fact]
    public void Unsigned_files_are_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"t2t-unsigned-{Guid.NewGuid():N}.msi");
        File.WriteAllText(path, "not an installer");
        try
        {
            Assert.False(Authenticode.IsSignedByInsideApp(path, out var detail));
            Assert.Contains("invalid signature", detail);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Set TALK2TIA_SIGNED_MSI to a released (signed) MSI to check the publisher verification.</summary>
    [Fact]
    public void Released_installers_are_signed_by_InsideApp()
    {
        var msi = Environment.GetEnvironmentVariable("TALK2TIA_SIGNED_MSI");
        if (string.IsNullOrEmpty(msi) || !File.Exists(msi))
        {
            return;
        }

        Assert.True(Authenticode.IsSignedByInsideApp(msi, out var detail), detail);
        Assert.Contains("INSIDEAPP", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Updates_and_tray_can_be_disabled()
    {
        var config = Talk2TiaConfig.Parse("{ \"updates\": \"OFF\", \"trayIcon\": false }");
        Assert.Equal(Talk2TiaConfig.UpdatesOff, config.Updates);
        Assert.False(config.TrayIcon);
        Assert.False(new UpdateService(config).Enabled);
        Assert.Equal(Talk2TiaConfig.UpdatesNotify, Talk2TiaConfig.Parse("{}").Updates);
        Assert.Throws<ArgumentException>(() => Talk2TiaConfig.Parse("{ \"updates\": \"always\" }"));
    }
}
