using System.Buffers.Binary;

namespace Stationary.Ftms;

internal ref struct FtmsFieldReader(ReadOnlySpan<byte> source)
{
    private readonly ReadOnlySpan<byte> source = source;
    private int offset;

    public int Consumed => offset;

    public bool TryReadUInt16(out ushort value)
    {
        value = 0;
        if (source.Length - offset < sizeof(ushort)) return false;
        value = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
        offset += sizeof(ushort);
        return true;
    }

    public bool TryReadInt16(out short value)
    {
        value = 0;
        if (source.Length - offset < sizeof(short)) return false;
        value = BinaryPrimitives.ReadInt16LittleEndian(source[offset..]);
        offset += sizeof(short);
        return true;
    }

    public bool TryReadByte(out byte value)
    {
        value = 0;
        if (source.Length == offset) return false;
        value = source[offset++];
        return true;
    }

    public bool TryReadUInt24(out uint value)
    {
        value = 0;
        if (source.Length - offset < 3) return false;
        value = (uint)(source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16));
        offset += 3;
        return true;
    }
}

internal ref struct FtmsFieldWriter(Span<byte> destination)
{
    private readonly Span<byte> destination = destination;
    private int offset;

    public int Written => offset;

    public void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], value);
        offset += sizeof(ushort);
    }

    public void WriteInt16(short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(destination[offset..], value);
        offset += sizeof(short);
    }

    public void WriteByte(byte value) => destination[offset++] = value;

    public void WriteUInt24(uint value)
    {
        destination[offset++] = (byte)value;
        destination[offset++] = (byte)(value >> 8);
        destination[offset++] = (byte)(value >> 16);
    }
}