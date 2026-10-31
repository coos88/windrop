using WinDrop.AirDrop;
using WinDrop.UI;
using WinDrop.Wsl;
using Xunit;

namespace WinDrop.Tests;

public class OwlyOutputTests
{
    [Fact]
    public void ParsesBrowseLine()
    {
        var peer = OwlyOutput.ParsePeer("""a1b2c3d4e5f6   "Coos’s iPhone" (fe80::1c2b:3cff:fe4d:5e6f%awdl0)""");
        Assert.NotNull(peer);
        Assert.Equal("a1b2c3d4e5f6", peer!.Instance);
        Assert.Equal("Coos’s iPhone", peer.Name);
        Assert.Equal("fe80::1c2b:3cff:fe4d:5e6f%awdl0", peer.Address);
    }

    [Fact]
    public void IgnoresOtherLines() => Assert.Null(OwlyOutput.ParsePeer("looking for a receiver for 10 s..."));

    [Fact]
    public void ParsesRequestHeaderAndFiles()
    {
        var header = OwlyOutput.ParseRequest("""\"iPhone van Coos\" (\"iPhone\") wants to send:""".Replace("\\\"", "\""));
        Assert.Equal(("iPhone van Coos", "iPhone"), header);

        var file = OwlyOutput.ParseFile("""  "IMG_0042.HEIC" public.heic 2451234 bytes""");
        Assert.Equal(new OfferedFile("IMG_0042.HEIC", "public.heic", 2451234), file);

        var noSize = OwlyOutput.ParseFile("""  "notes.txt" public.plain-text """);
        Assert.Equal(new OfferedFile("notes.txt", "public.plain-text", null), noSize);
    }

    [Theory]
    [InlineData("\"plain\"", "plain")]
    [InlineData("\"say \\\"hi\\\"\"", "say \"hi\"")]
    [InlineData("\"tab\\tnew\\nline\"", "tab\tnew\nline")]
    [InlineData("\"bell \\u{7}\"", "bell \u0007")]
    [InlineData("\"emoji \\u{1f600}\"", "emoji 😀")]
    public void UnquotesRustDebugStrings(string input, string expected) => Assert.Equal(expected, OwlyOutput.Unquote(input));
}

public class PathTests
{
    [Theory]
    [InlineData(@"C:\Users\coos\Downloads", "/mnt/c/Users/coos/Downloads")]
    [InlineData(@"D:\a b\c.txt", "/mnt/d/a b/c.txt")]
    public void ConvertsBothWays(string windows, string wsl)
    {
        Assert.Equal(wsl, Wsl.Wsl.ToWslPath(windows));
        Assert.Equal(windows, Wsl.Wsl.ToWindowsPath(wsl));
    }

    [Fact]
    public void LinuxOnlyPathHasNoWindowsPath() => Assert.Null(Wsl.Wsl.ToWindowsPath("/home/coos/x"));
}

public class UsbipdTests
{
    private const string Sample = """
        Connected:
        BUSID  VID:PID    DEVICE                                                        STATE
        1-4    048d:c101  USB Input Device                                              Not shared
        2-4    0489:e0cd  MediaTek Bluetooth Adapter                                    Not shared
        3-1    0e8d:7612  MediaTek 802.11ac WLAN                                        Shared
        7-3    046d:c08b  G502 HERO, USB Input Device, Virtual HID Framework (VHF) ...  Attached

        Persisted:
        GUID                                  DEVICE
        """;

    [Fact]
    public void ParsesList()
    {
        var list = UsbStick.ParseList(Sample);
        Assert.Equal(4, list.Count);
        var stick = Assert.Single(list, d => d.VidPid == "0e8d:7612");
        Assert.Equal("3-1", stick.BusId);
        Assert.True(stick.Shared);
        Assert.False(stick.Attached);
        Assert.True(list.Single(d => d.BusId == "7-3").Attached);
    }
}

public class WordingTests
{
    [Fact]
    public void DescribesOffers()
    {
        Assert.Equal("een foto", RequestWindow.Describe([new("IMG_1.HEIC", "public.heic", 1)]));
        Assert.Equal("2 foto's", RequestWindow.Describe([new("a.jpg", "public.jpeg", 1), new("b.png", "public.png", 1)]));
        Assert.Equal("2 bestanden", RequestWindow.Describe([new("a.jpg", "public.jpeg", 1), new("b.pdf", "com.adobe.pdf", 1)]));
    }

    [Theory]
    [InlineData("iPhone van Coos", "C")]
    [InlineData("Jane’s MacBook Pro", "J")]
    [InlineData("iPad", "I")]
    public void PicksInitials(string name, string expected) => Assert.Equal(expected, Theme.Initials(name));
}
