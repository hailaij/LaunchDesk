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
        byte[]? rented = null;
        ReadOnlySpan<byte> input = bytes;
        if (pending.Length > 0)
        {
            rented = ArrayPool<byte>.Shared.Rent(checked(pending.Length + bytes.Length));
            pending.CopyTo(rented, 0); bytes.CopyTo(rented.AsSpan(pending.Length));
            input = rented.AsSpan(0, pending.Length + bytes.Length);
        }
        try
        {
            var offset = 0;
            while (offset < input.Length)
            {
                var status = Rune.DecodeFromUtf8(input[offset..], out _, out var consumed);
                if (status == OperationStatus.NeedMoreData && !flush) break;
                if (status != OperationStatus.Done)
                {
                    decoder = fallback.GetDecoder(); pending = [];
                    return DecodeWith(decoder, input, flush);
                }
                offset += consumed;
            }
            pending = input[offset..].ToArray();
            return Encoding.UTF8.GetString(input[..offset]);
        }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
    }
    private static string DecodeWith(Decoder decoder, ReadOnlySpan<byte> bytes, bool flush)
    {
        var chars = ArrayPool<char>.Shared.Rent(checked(bytes.Length * 2 + 4));
        try
        {
            var count = decoder.GetChars(bytes, chars, flush);
            return new string(chars, 0, count);
        }
        finally { ArrayPool<char>.Shared.Return(chars); }
    }
    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
