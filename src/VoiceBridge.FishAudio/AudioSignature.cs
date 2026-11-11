using System.Buffers.Binary;

namespace VoiceBridge.FishAudio;

internal static class AudioSignature
{
    public static bool HasMp3Frame(ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        if (bytes.StartsWith("ID3"u8))
        {
            if (bytes.Length < 10 || bytes.Slice(6, 4).IndexOfAnyInRange((byte)128, byte.MaxValue) >= 0) return false;
            offset = 10 + (bytes[6] << 21) + (bytes[7] << 14) + (bytes[8] << 7) + bytes[9];
            if (bytes[3] == 4 && (bytes[5] & 16) != 0) offset += 10;
        }
        if (offset > bytes.Length - 4 || bytes[offset] != 255 || (bytes[offset + 1] & 224) != 224) return false;
        var version = (bytes[offset + 1] >> 3) & 3;
        if (version == 1 || ((bytes[offset + 1] >> 1) & 3) != 1) return false;
        ReadOnlySpan<int> rates = [44100, 48000, 32000];
        var rateIndex = (bytes[offset + 2] >> 2) & 3;
        if (rateIndex == 3) return false;
        var rate = rates[rateIndex] / (version == 3 ? 1 : version == 2 ? 2 : 4);
        ReadOnlySpan<int> mpeg1 = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];
        ReadOnlySpan<int> mpeg2 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];
        var bitrate = (version == 3 ? mpeg1 : mpeg2)[bytes[offset + 2] >> 4];
        return bitrate > 0 && offset + (version == 3 ? 144000 : 72000) * bitrate / rate + ((bytes[offset + 2] >> 1) & 1) <= bytes.Length;
    }

    public static bool IsCompleteWave(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes.StartsWith("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WAVE"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) != bytes.Length - 8) return false;
        var format = false;
        var data = false;
        var alignment = 0;
        uint dataLength = 0;
        var cursor = 12;
        while (cursor < bytes.Length)
        {
            if (bytes.Length - cursor < 8) return false;
            var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(cursor + 4, 4));
            if (count > bytes.Length - cursor - 8) return false;
            var chunk = bytes.Slice(cursor + 8, (int)count);
            if (bytes.Slice(cursor, 4).SequenceEqual("fmt "u8))
            {
                if (count < 16) return false;
                var tag = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(2));
                var rate = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(4));
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(14));
                if (tag is not (1 or 3 or 65534) || channels is < 1 or > 8 || rate is < 8000 or > 192000 || bits is not (8 or 16 or 24 or 32)) return false;
                alignment = channels * bits / 8;
                if (BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(12)) != alignment ||
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(8)) != rate * alignment || tag == 3 && bits != 32) return false;
                if (tag == 65534)
                {
                    if (count < 40 || BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(16)) < 22) return false;
                    var subtype = new Guid(chunk.Slice(24, 16));
                    if (subtype != new Guid("00000001-0000-0010-8000-00aa00389b71") &&
                        !(subtype == new Guid("00000003-0000-0010-8000-00aa00389b71") && bits == 32)) return false;
                }
                format = true;
            }
            if (bytes.Slice(cursor, 4).SequenceEqual("data"u8)) { data |= count > 0; dataLength = count; }
            cursor += 8 + (int)count + (int)(count & 1);
        }
        return cursor == bytes.Length && format && data && alignment > 0 && dataLength % alignment == 0;
    }

    /// <summary>
    /// Wrap headerless 16-bit little-endian PCM (what Gemini and several local
    /// servers return) in a RIFF header so the player can open it.
    /// </summary>
    public static byte[] WaveFromPcm16(ReadOnlySpan<byte> pcm, int sampleRate, int channels = 1)
    {
        if (sampleRate is < 8000 or > 192000 || channels is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        var alignment = channels * 2;
        var length = pcm.Length - pcm.Length % alignment;
        var bytes = new byte[44 + length];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 36 + length);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), sampleRate * alignment);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), (short)alignment);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), length);
        pcm[..length].CopyTo(bytes.AsSpan(44));
        return bytes;
    }

    /// <summary>
    /// Streaming servers often write a WAV header before they know the length and
    /// leave the size fields at 0 or 0xFFFFFFFF. Only that placeholder case is
    /// repaired; a header that promises more data than arrived stays refused.
    /// </summary>
    public static byte[]? RepairStreamedWave(byte[] bytes)
    {
        if (bytes.Length < 44 || !bytes.AsSpan().StartsWith("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            return null;
        var riff = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        var cursor = 12;
        int channels = 0, rate = 0, bits = 0;
        while (cursor + 8 <= bytes.Length)
        {
            var id = bytes.AsSpan(cursor, 4);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor + 4));
            if (id.SequenceEqual("fmt "u8))
            {
                if (count < 16 || cursor + 8 + 16 > bytes.Length) return null;
                if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor + 8)) != 1) return null;
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor + 10));
                rate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cursor + 12));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor + 22));
            }
            else if (id.SequenceEqual("data"u8))
            {
                var placeholder = count is 0 or uint.MaxValue || riff is 0 or uint.MaxValue;
                if (!placeholder || bits != 16 || channels is < 1 or > 8 || rate is < 8000 or > 192000) return null;
                var pcm = bytes.AsSpan(cursor + 8);
                return pcm.Length == 0 ? null : WaveFromPcm16(pcm, rate, channels);
            }
            if (count > (uint)(bytes.Length - cursor - 8)) return null;
            cursor += 8 + (int)count + (int)(count & 1);
        }
        return null;
    }
}
