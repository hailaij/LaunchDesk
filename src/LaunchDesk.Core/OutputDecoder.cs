using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace LaunchDesk.Core;

// Each output pipe has its own encoding decision and incomplete UTF-8 suffix.
public sealed class OutputDecoder
{
    private readonly Encoding fallback;
    private Decoder? decoder;
    private byte[] pending = [];
    public OutputDecoder(string name, Encoding? systemEncoding = null)
    {
        fallback = systemEncoding ?? SystemEncoding;
        if (!string.Equals(name, "auto", StringComparison.OrdinalIgnoreCase))
            decoder = Resolve(name).GetDecoder();
    }
    public static Encoding SystemEncoding => Encoding.GetEncoding((int)GetACP());
    public static Encoding Resolve(string name) => name.ToLowerInvariant() switch
    {
        "auto" => Encoding.UTF8,
        "system" => SystemEncoding,
        _ => Encoding.GetEncoding(name)
    };
    public string Decode(ReadOnlySpan<byte> bytes, bool flush = false)
    {
        if (decoder is not null) return DecodeWith(decoder, bytes, flush);
        var input = new byte[pending.Length + bytes.Length];
        pending.CopyTo(input, 0); bytes.CopyTo(input.AsSpan(pending.Length));
        var offset = 0;
        while (offset < input.Length)
        {
            var status = Rune.DecodeFromUtf8(input.AsSpan(offset), out _, out var consumed);
            if (status == OperationStatus.NeedMoreData && !flush) break;
            if (status != OperationStatus.Done)
            {
                decoder = fallback.GetDecoder(); pending = [];
                return DecodeWith(decoder, input, flush);
            }
            offset += consumed;
        }
        pending = input[offset..];
        return Encoding.UTF8.GetString(input.AsSpan(0, offset));
    }
    private static string DecodeWith(Decoder decoder, ReadOnlySpan<byte> bytes, bool flush)
    {
        var chars = new char[bytes.Length * 2 + 4];
        var count = decoder.GetChars(bytes, chars, flush);
        return new string(chars, 0, count);
    }
    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
