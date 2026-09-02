using System.Buffers.Binary;
using System.Text;

namespace Cs2Prak.Core.MySql;

[Flags]
public enum Capabilities : uint
{
    LongPassword = 1,
    FoundRows = 1 << 1,
    LongFlag = 1 << 2,
    ConnectWithDb = 1 << 3,
    NoSchema = 1 << 4,
    Compress = 1 << 5,
    LocalFiles = 1 << 7,
    IgnoreSpace = 1 << 8,
    Protocol41 = 1 << 9,
    Interactive = 1 << 10,
    Ssl = 1 << 11,
    Transactions = 1 << 13,
    SecureConnection = 1 << 15,
    MultiStatements = 1 << 16,
    MultiResults = 1 << 17,
    PsMultiResults = 1 << 18,
    PluginAuth = 1 << 19,
    ConnectAttrs = 1 << 20,
    PluginAuthLenEncClientData = 1 << 21,
    SessionTrack = 1 << 23,
    DeprecateEof = 1 << 24,
}

public enum ColumnType : byte
{
    Tiny = 0x01,
    Long = 0x03,
    Float = 0x04,
    Double = 0x05,
    LongLong = 0x08,
    VarString = 0xFD,
}

public enum Command : byte
{
    Quit = 0x01,
    InitDb = 0x02,
    Query = 0x03,
    FieldList = 0x04,
    Ping = 0x0E,
    StmtPrepare = 0x16,
    StmtClose = 0x19,
    SetOption = 0x1B,
    ResetConnection = 0x1F,
}

public sealed record ResultColumn(string Name, ColumnType Type, uint Length, ushort CharacterSet);

public sealed class PacketWriter
{
    private readonly MemoryStream _buffer = new(256);

    public int Length => (int)_buffer.Length;

    public byte[] ToArray() => _buffer.ToArray();

    public void Byte(byte value) => _buffer.WriteByte(value);

    public void Bytes(ReadOnlySpan<byte> value) => _buffer.Write(value);

    public void UInt16(ushort value)
    {
        Span<byte> tmp = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(tmp, value);
        _buffer.Write(tmp);
    }

    public void UInt32(uint value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);
        _buffer.Write(tmp);
    }

    public void Zeros(int count)
    {
        for (var i = 0; i < count; i++) _buffer.WriteByte(0);
    }

    public void NullTerminated(string value)
    {
        _buffer.Write(Encoding.UTF8.GetBytes(value));
        _buffer.WriteByte(0);
    }

    public void Rest(string value) => _buffer.Write(Encoding.UTF8.GetBytes(value));

    public void LengthEncoded(ulong value)
    {
        switch (value)
        {
            case < 251:
                _buffer.WriteByte((byte)value);
                break;
            case < 1UL << 16:
                _buffer.WriteByte(0xFC);
                UInt16((ushort)value);
                break;
            case < 1UL << 24:
                _buffer.WriteByte(0xFD);
                _buffer.WriteByte((byte)value);
                _buffer.WriteByte((byte)(value >> 8));
                _buffer.WriteByte((byte)(value >> 16));
                break;
            default:
                _buffer.WriteByte(0xFE);
                Span<byte> tmp = stackalloc byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(tmp, value);
                _buffer.Write(tmp);
                break;
        }
    }

    public void LengthEncodedString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        LengthEncoded((ulong)bytes.Length);
        _buffer.Write(bytes);
    }

    public void NullField() => _buffer.WriteByte(0xFB);
}

public ref struct PacketReader(ReadOnlySpan<byte> payload)
{
    private readonly ReadOnlySpan<byte> _payload = payload;
    private int _position = 0;

    public readonly int Remaining => _payload.Length - _position;

    public byte Byte() => _payload[_position++];

    public uint UInt32()
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_payload[_position..]);
        _position += 4;
        return value;
    }

    public void Skip(int count) => _position += count;

    public string NullTerminated()
    {
        var end = _payload[_position..].IndexOf((byte)0);
        if (end < 0) end = Remaining;
        var text = Encoding.UTF8.GetString(_payload.Slice(_position, end));
        _position += end + 1;
        return text;
    }

    public string Rest()
    {
        var text = Encoding.UTF8.GetString(_payload[_position..]);
        _position = _payload.Length;
        return text;
    }
}
