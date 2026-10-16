using System.Buffers.Binary;

namespace Temhalo.Core.Flv;

internal static class AvcAacInspector
{
    public static int CompositionTime(ReadOnlySpan<byte> payload)
    {
        int value = FlvTagReader.ReadUInt24(payload.Slice(2, 3));
        return (value & 0x800000) == 0 ? value : value | unchecked((int)0xFF000000);
    }

    public static int ValidateAvcConfiguration(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12 || payload[5] != 1 || (payload[9] & 0xFC) != 0xFC)
            throw new InvalidDataException("Configuration AVC invalide.");
        int lengthSize = (payload[9] & 3) + 1;
        if (lengthSize == 3) throw new InvalidDataException("Taille de longueur AVC réservée.");
        int cursor = 11, sps = payload[10] & 31;
        if (sps == 0) throw new InvalidDataException("SPS AVC manquant.");
        for (int i = 0; i < sps; i++) ReadParameterSet(payload, ref cursor, 7);
        if (cursor >= payload.Length) throw new InvalidDataException("PPS AVC manquant.");
        int pps = payload[cursor++];
        if (pps == 0) throw new InvalidDataException("PPS AVC manquant.");
        for (int i = 0; i < pps; i++) ReadParameterSet(payload, ref cursor, 8);
        return lengthSize;
    }

    private static void ReadParameterSet(ReadOnlySpan<byte> bytes, ref int cursor, int type)
    {
        if (cursor + 2 > bytes.Length) throw new InvalidDataException("Configuration AVC tronquée.");
        int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(cursor, 2));
        cursor += 2;
        if (length == 0 || length > bytes.Length - cursor || (bytes[cursor] & 31) != type)
            throw new InvalidDataException("Paramètre AVC invalide.");
        cursor += length;
    }

    public static bool IsIdr(ReadOnlySpan<byte> payload, int lengthSize)
    {
        if (payload.Length <= 5) throw new InvalidDataException("Image AVC vide.");
        int cursor = 5;
        bool idr = false;
        while (cursor < payload.Length)
        {
            if (lengthSize > payload.Length - cursor) throw new InvalidDataException("Longueur NAL tronquée.");
            uint length = 0;
            for (int i = 0; i < lengthSize; i++) length = (length << 8) | payload[cursor++];
            if (length == 0 || length > payload.Length - cursor || (payload[cursor] & 0x80) != 0)
                throw new InvalidDataException("Unité NAL invalide.");
            idr |= (payload[cursor] & 31) == 5;
            cursor += (int)length;
        }
        return idr && (payload[0] >> 4) == 1;
    }
}
