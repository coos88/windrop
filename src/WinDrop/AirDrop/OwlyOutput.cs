using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinDrop.AirDrop;

/// <summary>A receiver found by <c>owly browse</c>.</summary>
public sealed record Peer(string Instance, string Name, string Address);

/// <summary>A file in an incoming AirDrop request.</summary>
public sealed record OfferedFile(string Name, string Type, long? Size);

/// <summary>
/// Parses the text <c>owly</c> prints. owly prints names with Rust's
/// <c>{:?}</c>, so they arrive quoted and escaped.
/// </summary>
public static partial class OwlyOutput
{
    // owly browse: println!("{:<14} {:?} ({})", instance, name, addr)
    [GeneratedRegex("""^(?<inst>\S+)\s+(?<name>".*")\s+\((?<addr>[^()]*)\)\s*$""")]
    private static partial Regex PeerLine();

    // owly receive: eprintln!("{:?} ({:?}) wants to send:", computer, model)
    [GeneratedRegex("""^(?<who>".*") \((?<model>".*")\) wants to send:\s*$""")]
    private static partial Regex RequestLine();

    // owly receive: eprintln!("  {:?} {} {}", name, file_type, "<n> bytes")
    [GeneratedRegex("""^  (?<name>".*?(?<!\\)") (?<type>\S+)(?: (?<size>\d+) bytes)?\s*$""")]
    private static partial Regex FileLine();

    public static Peer? ParsePeer(string line)
    {
        var m = PeerLine().Match(line);
        if (!m.Success) return null;
        return new Peer(m.Groups["inst"].Value, Unquote(m.Groups["name"].Value), m.Groups["addr"].Value);
    }

    public static (string Sender, string Model)? ParseRequest(string line)
    {
        var m = RequestLine().Match(line);
        if (!m.Success) return null;
        return (Unquote(m.Groups["who"].Value), Unquote(m.Groups["model"].Value));
    }

    public static OfferedFile? ParseFile(string line)
    {
        var m = FileLine().Match(line);
        if (!m.Success) return null;
        long? size = m.Groups["size"].Success ? long.Parse(m.Groups["size"].Value, CultureInfo.InvariantCulture) : null;
        return new OfferedFile(Unquote(m.Groups["name"].Value), m.Groups["type"].Value, size);
    }

    /// <summary>Undoes Rust's <c>{:?}</c> string escaping.</summary>
    public static string Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            char e = s[++i];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case '0': sb.Append('\0'); break;
                case 'u' when i + 1 < s.Length && s[i + 1] == '{':
                    int end = s.IndexOf('}', i);
                    if (end > 0 && int.TryParse(s.AsSpan(i + 2, end - i - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp))
                    {
                        sb.Append(char.ConvertFromUtf32(cp));
                        i = end;
                    }
                    else sb.Append('u');
                    break;
                default: sb.Append(e); break; // \\ \" \'
            }
        }
        return sb.ToString();
    }
}
