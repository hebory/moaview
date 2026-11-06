using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MoaView.Services;

internal static class ClipStudioPreviewReader
{
    private const int ChunkHeaderSize = 16;
    private const int SqliteRow = 100;
    private const uint DeserializeReadOnly = 4;

    public static byte[] Read(byte[] data)
    {
        var source = data.AsSpan();
        if (source.Length < 24 || !source.StartsWith("CSFCHUNK"u8) ||
            BinaryPrimitives.ReadUInt64BigEndian(source[8..]) != (ulong)source.Length)
            throw new InvalidDataException();

        var headOffset = BinaryPrimitives.ReadUInt64BigEndian(source[16..]);
        var head = GetChunk(source, headOffset, "CHNKHead"u8);
        if (head.Length < 24)
            throw new InvalidDataException();
        var databaseOffset = BinaryPrimitives.ReadUInt64BigEndian(head[8..]);
        var database = GetChunk(source, databaseOffset, "CHNKSQLi"u8);
        if (!database.StartsWith("SQLite format 3\0"u8))
            throw new InvalidDataException();

        // SQLite may overread a small amount of padding in malformed pages.
        var padded = new byte[database.Length + 32];
        database.CopyTo(padded);
        return ReadPreviewBlob(padded, database.Length);
    }

    private static ReadOnlySpan<byte> GetChunk(ReadOnlySpan<byte> data, ulong offset, ReadOnlySpan<byte> tag)
    {
        if (offset < 24 || offset > (ulong)(data.Length - ChunkHeaderSize))
            throw new InvalidDataException();
        var chunk = data[(int)offset..];
        var length = BinaryPrimitives.ReadUInt64BigEndian(chunk[8..]);
        if (!chunk.StartsWith(tag) || length > (ulong)(chunk.Length - ChunkHeaderSize))
            throw new InvalidDataException();
        return chunk.Slice(ChunkHeaderSize, (int)length);
    }

    private static byte[] ReadPreviewBlob(byte[] data, int length)
    {
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        nint connection = 0;
        nint statement = 0;
        var timer = Stopwatch.StartNew();
        ProgressCallback abortAfterLimit = _ => timer.Elapsed > TimeSpan.FromSeconds(2) ? 1 : 0;
        try
        {
            Check(Sqlite.Open(":memory:", out connection));
            Check(Sqlite.Deserialize(connection, "main", pinned.AddrOfPinnedObject(), length, length, DeserializeReadOnly));
            Sqlite.Limit(connection, 0, PreviewLimits.MaxEntryBytes); // SQLITE_LIMIT_LENGTH
            Sqlite.ProgressHandler(connection, 1000, abortAfterLimit, 0);
            Check(Sqlite.Exec(connection, "PRAGMA trusted_schema=OFF; PRAGMA query_only=ON;", 0, 0, 0));
            const string query = "SELECT ImageData FROM main.CanvasPreview WHERE typeof(ImageData)='blob' LIMIT 1";
            Check(Sqlite.Prepare(connection, query, -1, out statement, 0));
            if (Sqlite.Step(statement) != SqliteRow)
                throw new InvalidDataException("CLIP 파일에 저장된 캔버스 미리보기가 없습니다.");
            var blob = Sqlite.ColumnBlob(statement, 0);
            var size = Sqlite.ColumnBytes(statement, 0);
            if (blob == 0 || size is < 8 or > PreviewLimits.MaxEntryBytes)
                throw new InvalidDataException();
            var png = new byte[size];
            Marshal.Copy(blob, png, 0, size);
            if (!png.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new InvalidDataException();
            return png;
        }
        finally
        {
            if (statement != 0) Sqlite.Finalize(statement);
            if (connection != 0) Sqlite.Close(connection);
            GC.KeepAlive(abortAfterLimit);
            pinned.Free();
        }
    }

    private static void Check(int result)
    {
        if (result != 0) throw new InvalidDataException();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ProgressCallback(nint context);

    private static class Sqlite
    {
        private const string Library = "winsqlite3.dll";

        [DllImport(Library, EntryPoint = "sqlite3_open", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out nint connection);
        [DllImport(Library, EntryPoint = "sqlite3_deserialize", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Deserialize(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string schema,
            nint data, long dataSize, long bufferSize, uint flags);
        [DllImport(Library, EntryPoint = "sqlite3_limit", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Limit(nint connection, int category, int limit);
        [DllImport(Library, EntryPoint = "sqlite3_progress_handler", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ProgressHandler(nint connection, int instructions, ProgressCallback callback, nint context);
        [DllImport(Library, EntryPoint = "sqlite3_exec", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Exec(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
            nint callback, nint context, nint error);
        [DllImport(Library, EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Prepare(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
            int byteCount, out nint statement, nint tail);
        [DllImport(Library, EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Step(nint statement);
        [DllImport(Library, EntryPoint = "sqlite3_column_blob", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint ColumnBlob(nint statement, int index);
        [DllImport(Library, EntryPoint = "sqlite3_column_bytes", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ColumnBytes(nint statement, int index);
        [DllImport(Library, EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Finalize(nint statement);
        [DllImport(Library, EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Close(nint connection);
    }
}
