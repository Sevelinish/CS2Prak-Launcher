using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Data.Sqlite;
using Cs2Prak.Core.Skins;

namespace Cs2Prak.Core.MySql;

public static class MySqlSqliteServer
{
    public const int Port = 3306;

    private static TcpListener? _listener;

    public static string Failure { get; private set; } = "";

    public static bool IsListening => _listener is not null;

    public static void Start()
    {
        if (_listener is not null) return;

        try
        {
            var listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
            _listener = listener;
            Failure = "";
            _ = AcceptLoopAsync(listener);
        }
        catch (SocketException e)
        {
            Failure = $"port {Port} unavailable: {e.Message}";
        }
    }

    private static async Task AcceptLoopAsync(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); }
            catch (Exception) { return; }

            _ = Task.Run(async () =>
            {
                using (client)
                {
                    try { await new Session(client).RunAsync(); }
                    catch (Exception) {  }
                }
            });
        }
    }

    internal static class ColumnTypes
    {
        private static readonly HashSet<string> Text = new(StringComparer.Ordinal)
        {
            "steamid", "weapon_nametag", "knife", "agent_ct", "agent_t",
            "weapon_sticker_0", "weapon_sticker_1", "weapon_sticker_2",
            "weapon_sticker_3", "weapon_sticker_4", "weapon_keychain",
        };

        private static readonly HashSet<string> Bool = new(StringComparer.Ordinal) { "weapon_stattrak" };

        private static readonly HashSet<string> Float = new(StringComparer.Ordinal) { "weapon_wear" };

        private const ushort Utf8 = 33;
        private const ushort Binary = 63;

        public static ResultColumn For(string name, IReadOnlyList<object?[]> rows, int index)
        {
            if (Bool.Contains(name)) return new ResultColumn(name, ColumnType.Tiny, 1, Binary);
            if (Float.Contains(name)) return new ResultColumn(name, ColumnType.Float, 12, Binary);
            if (Text.Contains(name)) return new ResultColumn(name, ColumnType.VarString, 1024, Utf8);

            foreach (var row in rows)
            {
                var value = row[index];
                if (value is null) continue;
                if (value is double or float) return new ResultColumn(name, ColumnType.Double, 22, Binary);
                if (value is long or int) return new ResultColumn(name, ColumnType.LongLong, 20, Binary);
                break;
            }
            return new ResultColumn(name, ColumnType.VarString, 1024, Utf8);
        }
    }

    private sealed class Session(TcpClient client)
    {
        private const Capabilities ServerCapabilities =
            Capabilities.LongPassword | Capabilities.FoundRows | Capabilities.LongFlag |
            Capabilities.ConnectWithDb | Capabilities.NoSchema | Capabilities.Protocol41 |
            Capabilities.Transactions | Capabilities.SecureConnection | Capabilities.MultiResults |
            Capabilities.PluginAuth | Capabilities.ConnectAttrs |
            Capabilities.PluginAuthLenEncClientData;

        private const ushort StatusAutocommit = 0x0002;

        private readonly NetworkStream _stream = client.GetStream();

        private byte _sequence;

        public async Task RunAsync()
        {
            await SendHandshakeAsync();

            if (await ReadPacketAsync() is null) return;

            await SendOkAsync();

            using var db = SkinsDatabase.Open();

            while (true)
            {
                var packet = await ReadPacketAsync();
                if (packet is null || packet.Length == 0) return;

                var command = (Command)packet[0];
                if (command == Command.Quit) return;

                await DispatchAsync(command, packet, db);
            }
        }

        private async Task DispatchAsync(Command command, byte[] packet, SqliteConnection db)
        {
            switch (command)
            {
                case Command.Query:
                    await QueryAsync(Encoding.UTF8.GetString(packet, 1, packet.Length - 1), db);
                    break;

                case Command.InitDb:
                case Command.Ping:
                case Command.ResetConnection:
                    await SendOkAsync();
                    break;

                case Command.FieldList:
                case Command.SetOption:
                    await SendEofAsync();
                    break;

                case Command.StmtPrepare:
                    await SendErrorAsync(1295, "Prepared statements are not supported by this server");
                    break;

                default:
                    await SendOkAsync();
                    break;
            }
        }

        private async Task QueryAsync(string sql, SqliteConnection db)
        {
            var statement = SqlTranslator.ToSqlite(sql);
            if (statement is null)
            {
                await SendOkAsync();
                return;
            }

            try
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = statement;
                using var reader = cmd.ExecuteReader();

                if (reader.FieldCount == 0)
                {
                    await SendOkAsync((ulong)Math.Max(reader.RecordsAffected, 0));
                    return;
                }

                var names = new string[reader.FieldCount];
                for (var i = 0; i < names.Length; i++) names[i] = reader.GetName(i);

                var rows = new List<object?[]>();
                while (reader.Read())
                {
                    var row = new object?[names.Length];
                    for (var i = 0; i < names.Length; i++)
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }

                var columns = new ResultColumn[names.Length];
                for (var i = 0; i < names.Length; i++) columns[i] = ColumnTypes.For(names[i], rows, i);

                await SendResultSetAsync(columns, rows);
            }
            catch (Exception e)
            {
                LastQueryError = $"{e.Message} | {Collapse(statement)}";
                await SendOkAsync();
            }
        }

        private static string Collapse(string sql)
        {
            var flat = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return flat.Length > 200 ? flat[..200] : flat;
        }

        private async Task SendHandshakeAsync()
        {
            var scramble = new byte[20];
            Random.Shared.NextBytes(scramble);
            for (var i = 0; i < scramble.Length; i++)
                if (scramble[i] == 0) scramble[i] = 1;

            var w = new PacketWriter();
            w.Byte(10);
            w.NullTerminated("8.0.32-cs2prak");
            w.UInt32(1);
            w.Bytes(scramble.AsSpan(0, 8));
            w.Byte(0);
            w.UInt16((ushort)((uint)ServerCapabilities & 0xFFFF));
            w.Byte(255);
            w.UInt16(StatusAutocommit);
            w.UInt16((ushort)(((uint)ServerCapabilities >> 16) & 0xFFFF));
            w.Byte(21);
            w.Zeros(10);
            w.Bytes(scramble.AsSpan(8, 12));
            w.Byte(0);
            w.NullTerminated("mysql_native_password");

            await WritePacketAsync(w.ToArray());
        }

        private Task SendOkAsync(ulong affectedRows = 0)
        {
            var w = new PacketWriter();
            w.Byte(0x00);
            w.LengthEncoded(affectedRows);
            w.LengthEncoded(0);
            w.UInt16(StatusAutocommit);
            w.UInt16(0);
            return WritePacketAsync(w.ToArray());
        }

        private Task SendEofAsync()
        {
            var w = new PacketWriter();
            w.Byte(0xFE);
            w.UInt16(0);
            w.UInt16(StatusAutocommit);
            return WritePacketAsync(w.ToArray());
        }

        private Task SendErrorAsync(ushort code, string message)
        {
            var w = new PacketWriter();
            w.Byte(0xFF);
            w.UInt16(code);
            w.Byte((byte)'#');
            w.Rest("HY000");
            w.Rest(message);
            return WritePacketAsync(w.ToArray());
        }

        private async Task SendResultSetAsync(IReadOnlyList<ResultColumn> columns,
                                              IReadOnlyList<object?[]> rows)
        {
            var header = new PacketWriter();
            header.LengthEncoded((ulong)columns.Count);
            await WritePacketAsync(header.ToArray());

            foreach (var column in columns)
                await WritePacketAsync(ColumnDefinition(column));

            await SendEofAsync();

            foreach (var row in rows)
            {
                var w = new PacketWriter();
                for (var i = 0; i < columns.Count; i++)
                {
                    var text = Render(row[i]);
                    if (text is null) w.NullField();
                    else w.LengthEncodedString(text);
                }
                await WritePacketAsync(w.ToArray());
            }

            await SendEofAsync();
        }

        private static byte[] ColumnDefinition(ResultColumn column)
        {
            var w = new PacketWriter();
            w.LengthEncodedString("def");
            w.LengthEncodedString("");
            w.LengthEncodedString("");
            w.LengthEncodedString("");
            w.LengthEncodedString(column.Name);
            w.LengthEncodedString(column.Name);
            w.LengthEncoded(0x0C);
            w.UInt16(column.CharacterSet);
            w.UInt32(column.Length);
            w.Byte((byte)column.Type);
            w.UInt16(0);
            w.Byte(column.Type is ColumnType.Float or ColumnType.Double ? (byte)31 : (byte)0);
            w.UInt16(0);
            return w.ToArray();
        }

        private static string? Render(object? value) => value switch
        {
            null => null,
            bool b => b ? "1" : "0",
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        };

        private async Task<byte[]?> ReadPacketAsync()
        {
            var header = new byte[4];
            var read = 0;
            while (read < 4)
            {
                var n = await _stream.ReadAsync(header.AsMemory(read, 4 - read));
                if (n == 0) return null;
                read += n;
            }

            var length = header[0] | (header[1] << 8) | (header[2] << 16);
            _sequence = (byte)(header[3] + 1);

            var payload = new byte[length];
            read = 0;
            while (read < length)
            {
                var n = await _stream.ReadAsync(payload.AsMemory(read, length - read));
                if (n == 0) return null;
                read += n;
            }
            return payload;
        }

        private async Task WritePacketAsync(byte[] payload)
        {
            const int max = 0xFFFFFF;
            var offset = 0;

            while (true)
            {
                var chunk = Math.Min(max, payload.Length - offset);
                var frame = new byte[4 + chunk];
                frame[0] = (byte)chunk;
                frame[1] = (byte)(chunk >> 8);
                frame[2] = (byte)(chunk >> 16);
                frame[3] = _sequence++;
                Array.Copy(payload, offset, frame, 4, chunk);
                await _stream.WriteAsync(frame);
                offset += chunk;

                if (chunk < max) break;
            }

            await _stream.FlushAsync();
        }
    }

    public static string LastQueryError { get; private set; } = "";
}
