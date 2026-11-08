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
}
