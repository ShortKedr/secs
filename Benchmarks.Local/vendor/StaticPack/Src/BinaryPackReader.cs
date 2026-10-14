using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using static System.Runtime.CompilerServices.MethodImplOptions;
#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace FFS.Libraries.StaticPack {

    /// <summary>
    /// Parses a format-specific header and returns the total payload size in bytes
    /// (header + payload). Passed to <see cref="BinaryPackReader.RentAndFillFromBytes"/> /
    /// <see cref="BinaryPackReader.RentAndFillFromFile"/> for cases when the source is gzip-compressed
    /// and the exact size can only be determined by decompressing the first bytes.
    /// </summary>
    public delegate uint TotalSizeParser(ReadOnlySpan<byte> header);

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    #endif
    public struct BinaryPackReader : IDisposable {
        private const uint ARRAY_INFO_BYTES = sizeof(int) + sizeof(int);      // count + byteSize
        private const uint ARRAY2_INFO_BYTES = sizeof(int) * 2 + sizeof(int); // count x2 + byteSize
        private const uint ARRAY3_INFO_BYTES = sizeof(int) * 3 + sizeof(int); // count x3 + byteSize

        public byte[] Buffer;
        public readonly uint Size;
        public uint Position;
        public readonly bool Rented;

        [MethodImpl(AggressiveInlining)]
        public BinaryPackReader(byte[] buffer, uint size, uint position) : this(buffer, size, position, false) { }

        [MethodImpl(AggressiveInlining)]
        private BinaryPackReader(byte[] buffer, uint size, uint position, bool rented) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (buffer == null) throw new Exception("buffer is null");
            if (position + size > buffer.Length) throw new Exception("incorrect position or size");
            #endif
            Buffer = buffer;
            Position = position;
            Size = size;
            Rented = rented;
        }

        private const int MAX_PEEK_HEADER_SIZE = 256;

        // RFC 1952: every gzip stream starts with 0x1F 0x8B. Used to autodetect gzip-compressed sources
        // without requiring the caller to pass a flag.
        private const byte GZIP_MAGIC_0 = 0x1F;
        private const byte GZIP_MAGIC_1 = 0x8B;

        [MethodImpl(AggressiveInlining)]
        private static bool IsGzipMagic(byte b0, byte b1) {
            return b0 == GZIP_MAGIC_0 && b1 == GZIP_MAGIC_1;
        }

        /// <summary>
        /// Rents a buffer from <see cref="ArrayPool{Byte}.Shared"/> of exactly <paramref name="size"/> bytes
        /// and reads that many bytes from <paramref name="source"/> into it. The returned reader has
        /// <c>Rented = true</c> and must be released via <see cref="Dispose"/> to return the buffer to the pool.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public static BinaryPackReader RentAndFillFromStream(Stream source, uint size) {
            var buffer = ArrayPool<byte>.Shared.Rent((int) size);
            ReadExactly(source, buffer, 0, (int) size);
            return new BinaryPackReader(buffer, size, 0, true);
        }

        /// <summary>
        /// Creates a reader over the contents of <paramref name="data"/>. Gzip compression is autodetected
        /// from the standard RFC 1952 magic bytes (<c>0x1F 0x8B</c>) at offset 0.
        /// <para>When <paramref name="data"/> is not gzip-compressed the array is wrapped without renting
        /// (<c>Rented = false</c>); <paramref name="headerSize"/> and <paramref name="parseTotalSize"/> are ignored.</para>
        /// <para>When <paramref name="data"/> is gzip-compressed the method opens a decompression stream,
        /// peeks the first <paramref name="headerSize"/> bytes, passes them to <paramref name="parseTotalSize"/>,
        /// rents a buffer of exactly the returned size, copies the header and reads the remaining payload
        /// (<c>Rented = true</c>).</para>
        /// </summary>
        public static BinaryPackReader RentAndFillFromBytes(byte[] data, int headerSize = 0, TotalSizeParser parseTotalSize = null) {
            if (data.Length < 2 || !IsGzipMagic(data[0], data[1])) {
                return new BinaryPackReader(data, (uint) data.Length, 0);
            }
            using var ms = new MemoryStream(data, writable: false);
            using var gz = new GZipStream(ms, CompressionMode.Decompress, false);
            return RentAndFillWithHeaderPeek(gz, headerSize, parseTotalSize);
        }

        /// <summary>
        /// Creates a reader over the contents of a file. Gzip compression is autodetected from the
        /// standard RFC 1952 magic bytes (<c>0x1F 0x8B</c>) at offset 0.
        /// <para>When the file is not gzip-compressed it is read into a rented buffer of exactly its length
        /// (<c>Rented = true</c>); <paramref name="headerSize"/> and <paramref name="parseTotalSize"/> are ignored.</para>
        /// <para>When the file is gzip-compressed a decompression stream is opened, the first
        /// <paramref name="headerSize"/> bytes are peeked and passed to <paramref name="parseTotalSize"/>,
        /// a buffer of exactly the returned size is rented, the header is copied in and the remaining payload
        /// is read (<c>Rented = true</c>).</para>
        /// </summary>
        public static BinaryPackReader RentAndFillFromFile(string filePath, int headerSize = 0, TotalSizeParser parseTotalSize = null) {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None, 4096);
            Span<byte> magic = stackalloc byte[2];
            var magicRead = fs.Read(magic);
            if (magicRead < 2 || !IsGzipMagic(magic[0], magic[1])) {
                fs.Seek(0, SeekOrigin.Begin);
                return RentAndFillFromStream(fs, (uint) fs.Length);
            }
            fs.Seek(0, SeekOrigin.Begin);
            using var gz = new GZipStream(fs, CompressionMode.Decompress, false);
            return RentAndFillWithHeaderPeek(gz, headerSize, parseTotalSize);
        }

        private static BinaryPackReader RentAndFillWithHeaderPeek(Stream source, int headerSize, TotalSizeParser parseTotalSize) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (headerSize <= 0) throw new Exception("headerSize must be > 0 for gzip source");
            if (headerSize > MAX_PEEK_HEADER_SIZE) throw new Exception($"headerSize must be <= {MAX_PEEK_HEADER_SIZE}");
            if (parseTotalSize == null) throw new Exception("parseTotalSize is required for gzip source");
            #endif
            Span<byte> headerBuffer = stackalloc byte[MAX_PEEK_HEADER_SIZE];
            var header = headerBuffer.Slice(0, headerSize);
            ReadExactly(source, header);
            var totalSize = parseTotalSize(header);
            var buffer = ArrayPool<byte>.Shared.Rent((int) totalSize);
            header.CopyTo(buffer.AsSpan(0, headerSize));
            ReadExactly(source, buffer, headerSize, (int) totalSize - headerSize);
            return new BinaryPackReader(buffer, totalSize, 0, true);
        }

        /// <summary>
        /// Returns the rented buffer to <see cref="ArrayPool{Byte}.Shared"/> when the reader was created
        /// via one of the <c>RentAndFill*</c> factories. For readers that wrap an externally owned array
        /// this is a no-op.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public void Dispose() {
            if (Rented && Buffer != null) {
                ArrayPool<byte>.Shared.Return(Buffer);
            }
            Buffer = null;
        }

        /// <summary>
        /// Reads exactly <c>count</c> bytes in a loop: <see cref="Stream.Read(byte[], int, int)"/> may return
        /// fewer bytes than requested. <c>net7.0+</c> ships its own <c>Stream.ReadExactly</c>; this is a single
        /// wrapper that works on every target framework.
        /// </summary>
        public static void ReadExactly(Stream source, byte[] buffer, int offset, int count) {
            while (count > 0) {
                var n = source.Read(buffer, offset, count);
                if (n == 0) {
                    throw new EndOfStreamException();
                }
                offset += n;
                count -= n;
            }
        }

        /// <inheritdoc cref="ReadExactly(Stream, byte[], int, int)"/>
        public static void ReadExactly(Stream source, Span<byte> destination) {
            while (!destination.IsEmpty) {
                var n = source.Read(destination);
                if (n == 0) {
                    throw new EndOfStreamException();
                }
                destination = destination.Slice(n);
            }
        }

        [MethodImpl(AggressiveInlining)]
        public BinaryPackWriter AsWriter() {
            return BinaryPackWriter.Create(Buffer, Size);
        }

        [MethodImpl(AggressiveInlining)]
        public BinaryPackReader AsReader(uint position) {
            return new BinaryPackReader(Buffer, Size, position);
        }

        [MethodImpl(AggressiveInlining)]
        public BinaryPackWriter AsWriterCompact() {
            if (Position == Size) {
                return BinaryPackWriter.Create(Buffer, 0);
            }

            var count = Size - Position;
            Array.Copy(Buffer, Position, Buffer, 0, count);
            var writer = BinaryPackWriter.Create(Buffer, count);
            Buffer = null;
            return writer;
        }

        [MethodImpl(AggressiveInlining)]
        public bool HasNext() {
            return Position + 1 <= Size;
        }

        [MethodImpl(AggressiveInlining)]
        public bool HasNext(uint bytesCount) {
            return Position + bytesCount <= Size;
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipNext() {
            Position++;
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipNext(uint bytesCount) {
            Position += bytesCount;
        }

        [MethodImpl(AggressiveInlining)]
        public bool ReadNullFlag() {
            return Buffer[Position++] == 0;
        }

        #region PRIMITIVES
        [MethodImpl(AggressiveInlining)]
        public byte ReadByte() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(byte))) throw new Exception("ByteReader, Out of bound");
            #endif
            return Buffer[Position++];
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadByte(out byte value) {
            if (HasNext(sizeof(byte))) {
                value = Buffer[Position++];
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public sbyte ReadSByte() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(byte))) throw new Exception("ByteReader, Out of bound");
            #endif
            return (sbyte) Buffer[Position++];
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadSByte(out sbyte value) {
            if (HasNext(sizeof(sbyte))) {
                value = (sbyte) Buffer[Position++];
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public bool ReadBool() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(bool))) throw new Exception("ByteReader, Out of bound");
            #endif
            return Buffer[Position++] != 0;
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadBool(out bool value) {
            if (HasNext(sizeof(bool))) {
                value = Buffer[Position++] != 0;
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public short ReadShort() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(short))) throw new Exception("ByteReader, Out of bound");
            #endif
            var value = Unsafe.ReadUnaligned<short>(ref Buffer[Position]);
            Position += 2;
            return value;
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadShort(out short value) {
            if (HasNext(sizeof(short))) {
                value = ReadShort();
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public ushort ReadUshort() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(ushort))) throw new Exception("ByteReader, Out of bound");
            #endif
            var value = Unsafe.ReadUnaligned<ushort>(ref Buffer[Position]);
            Position += 2;
            return value;
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadUshort(out ushort value) {
            if (HasNext(sizeof(ushort))) {
                value = ReadUshort();
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public char ReadChar() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(char))) throw new Exception("ByteReader, Out of bound");
            #endif
            var value = Unsafe.ReadUnaligned<char>(ref Buffer[Position]);
            Position += 2;
            return value;
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadChar(out char value) {
            if (HasNext(sizeof(char))) {
                value = ReadChar();
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public int ReadInt() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(int))) throw new Exception("ByteReader, Out of bound");
            #endif
            var value = Unsafe.ReadUnaligned<int>(ref Buffer[Position]);
            Position += 4;
            return value;
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadInt(out int value) {
            if (HasNext(sizeof(int))) {
                value = ReadInt();
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public uint ReadUint() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(uint))) throw new Exception("ByteReader, Out of bound");
            #endif
            var value = Unsafe.ReadUnaligned<uint>(ref Buffer[Position]);
            Position += 4;
            return value;
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadUint(out uint value) {
            if (HasNext(sizeof(uint))) {
                value = ReadUint();
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public int ReadVarInt() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext()) throw new Exception("ByteReader, Out of bound");
            #endif
            var b0 = Buffer[Position++];
            if ((b0 & 0x80) == 0) return b0;

            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext()) throw new Exception("ByteReader, Out of bound");
            #endif
            var b1 = Buffer[Position++];
            if ((b1 & 0x80) == 0) return (b0 & 0x7F) | (b1 << 7);

            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext()) throw new Exception("ByteReader, Out of bound");
            #endif
            var b2 = Buffer[Position++];
            if ((b2 & 0x80) == 0) return (b0 & 0x7F) | ((b1 & 0x7F) << 7) | (b2 << 14);

            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext()) throw new Exception("ByteReader, Out of bound");
            #endif
            var b3 = Buffer[Position++];
            if ((b3 & 0x80) == 0) return (b0 & 0x7F) | ((b1 & 0x7F) << 7) | ((b2 & 0x7F) << 14) | (b3 << 21);

            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext()) throw new Exception("ByteReader, Out of bound");
            #endif
            var b4 = Buffer[Position++];
            return (b0 & 0x7F) | ((b1 & 0x7F) << 7) | ((b2 & 0x7F) << 14) | ((b3 & 0x7F) << 21) | (b4 << 28);
        }

        [MethodImpl(AggressiveInlining)]
        public short ReadVarShort() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext()) throw new Exception("ByteReader, Out of bound");
            #endif
            var b0 = Buffer[Position++];
            if ((b0 & 0b10000000) == 0) {
                return b0;
            }

            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext()) throw new Exception("ByteReader, Out of bound");
            #endif
            var b1 = Buffer[Position++];
            return (short) ((b0 & 0b1111111) | (b1 << 7));
        }

        [MethodImpl(AggressiveInlining)]
        public long ReadLong() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(long))) throw new Exception("ByteReader, Out of bound");
            #endif
            var value = Unsafe.ReadUnaligned<long>(ref Buffer[Position]);
            Position += 8;
            return value;
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadLong(out long value) {
            if (HasNext(sizeof(long))) {
                value = ReadLong();
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong ReadUlong() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(ulong))) throw new Exception("ByteReader, Out of bound");
            #endif
            var value = Unsafe.ReadUnaligned<ulong>(ref Buffer[Position]);
            Position += 8;
            return value;
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadUlong(out ulong value) {
            if (HasNext(sizeof(ulong))) {
                value = ReadUlong();
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public float ReadFloat() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(float))) throw new Exception("ByteReader, Out of bound");
            #endif
            var value = Unsafe.ReadUnaligned<float>(ref Buffer[Position]);
            Position += 4;
            return value;
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadFloat(out float value) {
            if (HasNext(sizeof(float))) {
                value = ReadFloat();
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public double ReadDouble() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(double))) throw new Exception("ByteReader, Out of bound");
            #endif
            var value = Unsafe.ReadUnaligned<double>(ref Buffer[Position]);
            Position += 8;
            return value;
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadDouble(out double value) {
            if (HasNext(sizeof(double))) {
                value = ReadDouble();
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadByte(out byte v0, out byte v1) {
            v0 = ReadByte();
            v1 = ReadByte();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadByte(out byte v0, out byte v1, out byte v2) {
            v0 = ReadByte();
            v1 = ReadByte();
            v2 = ReadByte();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadByte(out byte v0, out byte v1, out byte v2, out byte v3) {
            v0 = ReadByte();
            v1 = ReadByte();
            v2 = ReadByte();
            v3 = ReadByte();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadShort(out short v0, out short v1) {
            v0 = ReadShort();
            v1 = ReadShort();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadShort(out short v0, out short v1, out short v2) {
            v0 = ReadShort();
            v1 = ReadShort();
            v2 = ReadShort();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadShort(out short v0, out short v1, out short v2, out short v3) {
            v0 = ReadShort();
            v1 = ReadShort();
            v2 = ReadShort();
            v3 = ReadShort();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUshort(out ushort v0, out ushort v1) {
            v0 = ReadUshort();
            v1 = ReadUshort();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUshort(out ushort v0, out ushort v1, out ushort v2) {
            v0 = ReadUshort();
            v1 = ReadUshort();
            v2 = ReadUshort();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUshort(out ushort v0, out ushort v1, out ushort v2, out ushort v3) {
            v0 = ReadUshort();
            v1 = ReadUshort();
            v2 = ReadUshort();
            v3 = ReadUshort();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadInt(out int v0, out int v1) {
            v0 = ReadInt();
            v1 = ReadInt();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadInt(out int v0, out int v1, out int v2) {
            v0 = ReadInt();
            v1 = ReadInt();
            v2 = ReadInt();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadInt(out int v0, out int v1, out int v2, out int v3) {
            v0 = ReadInt();
            v1 = ReadInt();
            v2 = ReadInt();
            v3 = ReadInt();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUint(out uint v0, out uint v1) {
            v0 = ReadUint();
            v1 = ReadUint();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUint(out uint v0, out uint v1, out uint v2) {
            v0 = ReadUint();
            v1 = ReadUint();
            v2 = ReadUint();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUint(out uint v0, out uint v1, out uint v2, out uint v3) {
            v0 = ReadUint();
            v1 = ReadUint();
            v2 = ReadUint();
            v3 = ReadUint();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadFloat(out float v0, out float v1) {
            v0 = ReadFloat();
            v1 = ReadFloat();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadFloat(out float v0, out float v1, out float v2) {
            v0 = ReadFloat();
            v1 = ReadFloat();
            v2 = ReadFloat();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadFloat(out float v0, out float v1, out float v2, out float v3) {
            v0 = ReadFloat();
            v1 = ReadFloat();
            v2 = ReadFloat();
            v3 = ReadFloat();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadLong(out long v0, out long v1) {
            v0 = ReadLong();
            v1 = ReadLong();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadLong(out long v0, out long v1, out long v2) {
            v0 = ReadLong();
            v1 = ReadLong();
            v2 = ReadLong();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadLong(out long v0, out long v1, out long v2, out long v3) {
            v0 = ReadLong();
            v1 = ReadLong();
            v2 = ReadLong();
            v3 = ReadLong();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUlong(out ulong v0, out ulong v1) {
            v0 = ReadUlong();
            v1 = ReadUlong();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUlong(out ulong v0, out ulong v1, out ulong v2) {
            v0 = ReadUlong();
            v1 = ReadUlong();
            v2 = ReadUlong();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUlong(out ulong v0, out ulong v1, out ulong v2, out ulong v3) {
            v0 = ReadUlong();
            v1 = ReadUlong();
            v2 = ReadUlong();
            v3 = ReadUlong();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadDouble(out double v0, out double v1) {
            v0 = ReadDouble();
            v1 = ReadDouble();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadDouble(out double v0, out double v1, out double v2) {
            v0 = ReadDouble();
            v1 = ReadDouble();
            v2 = ReadDouble();
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadDouble(out double v0, out double v1, out double v2, out double v3) {
            v0 = ReadDouble();
            v1 = ReadDouble();
            v2 = ReadDouble();
            v3 = ReadDouble();
        }
        #endregion

        #region BASE_VALUE_TYPES
        [MethodImpl(AggressiveInlining)]
        public T? ReadNullable<T>() where T : struct {
            if (ReadNullFlag()) return null;

            return BinaryPack<T>.Read(ref this);
        }

        [MethodImpl(AggressiveInlining)]
        public Guid ReadGuid() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(16)) throw new Exception("ByteReader, Out of bound");
            #endif
            unsafe {
                fixed (byte* src = &Buffer[Position]) {
                    var guid = *(Guid*) src;
                    Position += 16;
                    return guid;
                }
            }
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadGuid(out Guid value) {
            if (HasNext(16)) {
                value = ReadGuid();
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(AggressiveInlining)]
        public DateTime ReadDateTime() {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(sizeof(long))) throw new Exception("ByteReader, Out of bound");
            #endif
            return new DateTime(ReadLong(), DateTimeKind.Utc);
        }

        [MethodImpl(AggressiveInlining)]
        public bool TryReadDateTime(out DateTime value) {
            if (HasNext(sizeof(long))) {
                value = ReadDateTime();
                return true;
            }

            value = default;
            return false;
        }
        #endregion

        #region STRING
        [MethodImpl(AggressiveInlining)]
        public string ReadString32() {
            if (ReadNullFlag()) return null;

            var byteCount = ReadInt();
            var result = Encoding.UTF8.GetString(Buffer, (int) Position, byteCount);
            Position += (uint) byteCount;
            return result;
        }

        [MethodImpl(AggressiveInlining)]
        public string ReadString16() {
            if (ReadNullFlag()) return null;

            var byteCount = ReadUshort();
            var result = Encoding.UTF8.GetString(Buffer, (int) Position, byteCount);
            Position += byteCount;
            return result;
        }

        [MethodImpl(AggressiveInlining)]
        public string ReadString8() {
            if (ReadNullFlag()) return null;

            var byteCount = ReadByte();
            var result = Encoding.UTF8.GetString(Buffer, (int) Position, byteCount);
            Position += byteCount;
            return result;
        }
        #endregion

        #region COLLECTIONS
        [MethodImpl(AggressiveInlining)]
        public T[] ReadArrayUnmanaged<T>() where T : unmanaged {
            if (ReadNullFlag()) return null;

            var count = ReadInt();
            var byteSize = ReadUint();
            var result = new T[count];

            if (count > 0) {
                unsafe {
                    #if DEBUG || FFS_PACK_ENABLE_DEBUG
                    var actualSize = (uint) (count * sizeof(T));
                    if (byteSize != actualSize) throw new Exception($"[ReadArrayUnmanaged<{typeof(T)}>] The number of bytes has changed - stored {byteSize}, actual {actualSize}");
                    #endif

                    fixed (byte* bytePtr = &Buffer[Position]) {
                        fixed (void* dataPtr = &result[0]) {
                            System.Buffer.MemoryCopy(bytePtr, dataPtr, byteSize, byteSize);
                        }
                    }

                    Position += byteSize;
                }
            }

            return result;
        }
        
        [MethodImpl(AggressiveInlining)]
        public ArraySegment<T> ReadArrayUnmanagedPooled<T>(out ArrayPoolHandle<T> poolHandle) where T : unmanaged {
            poolHandle = default;
            if (ReadNullFlag()) return default;

            var count = ReadInt();
            var byteSize = ReadUint();
            T[] result;
            
            if (count > 0) {
                result = ArrayPool<T>.Shared.Rent(count);
                poolHandle = new ArrayPoolHandle<T>(result);
                unsafe {
                    #if DEBUG || FFS_PACK_ENABLE_DEBUG
                    var actualSize = (uint) (count * sizeof(T));
                    if (byteSize != actualSize) throw new Exception($"[ReadArrayUnmanaged<{typeof(T)}>] The number of bytes has changed - stored {byteSize}, actual {actualSize}");
                    #endif

                    fixed (byte* bytePtr = &Buffer[Position]) {
                        fixed (void* dataPtr = &result[0]) {
                            System.Buffer.MemoryCopy(bytePtr, dataPtr, byteSize, byteSize);
                        }
                    }

                    Position += byteSize;
                }
            } else {
                result = Array.Empty<T>();
            }

            return new ArraySegment<T>(result, 0, count);
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadArrayUnmanaged<T>(ref T[] result) where T : unmanaged {
            if (ReadNullFlag()) {
                result = null;
            } else {
                var count = ReadInt();
                var byteSize = ReadUint();
                if (result == null || count > result.Length) {
                    result = new T[count];
                }

                if (count > 0) {
                    unsafe {
                        #if DEBUG || FFS_PACK_ENABLE_DEBUG
                        var actualSize = (uint) (count * sizeof(T));
                        if (byteSize != actualSize) throw new Exception($"[ReadArrayUnmanaged<{typeof(T)}>] The number of bytes has changed - stored {byteSize}, actual {actualSize}");
                        #endif

                        fixed (byte* bytePtr = &Buffer[Position]) {
                            fixed (void* dataPtr = &result[0]) {
                                System.Buffer.MemoryCopy(bytePtr, dataPtr, byteSize, byteSize);
                            }
                        }

                        Position += byteSize;
                    }
                }
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadArrayUnmanaged<T>(ref T[] result, int idx) where T : unmanaged {
            if (ReadNullFlag()) {
                result = null;
            } else {
                var count = ReadInt();
                var byteSize = ReadUint();
                if (result == null || count + idx > result.Length) {
                    result = new T[count + idx];
                }

                if (count > 0) {
                    unsafe {
                        #if DEBUG || FFS_PACK_ENABLE_DEBUG
                        var actualSize = (uint) (count * sizeof(T));
                        if (byteSize != actualSize) throw new Exception($"[ReadArrayUnmanaged<{typeof(T)}>] The number of bytes has changed - stored {byteSize}, actual {actualSize}");
                        #endif

                        fixed (byte* bytePtr = &Buffer[Position]) {
                            fixed (void* dataPtr = &result[idx]) {
                                System.Buffer.MemoryCopy(bytePtr, dataPtr, byteSize, byteSize);
                            }
                        }

                        Position += byteSize;
                    }
                }
            }
        }

        #if !FFS_PACK_DISABLE_MULTI_ARRAYS && !UNITY_WEBGL
        [MethodImpl(AggressiveInlining)]
        public T[,] ReadArray2DUnmanaged<T>() where T : unmanaged {
            if (ReadNullFlag()) return null;

            var dim0 = ReadInt();
            var dim1 = ReadInt();
            var byteSize = ReadUint();

            var res = new T[dim0, dim1];

            if (dim0 == 0 || dim1 == 0) {
                return res;
            }

            unsafe {
                #if DEBUG || FFS_PACK_ENABLE_DEBUG
                var actualSize = (uint) (dim0 * dim1 * sizeof(T));
                if (byteSize != actualSize) throw new Exception($"[ReadArray2Unmanaged<{typeof(T)}>] The number of bytes has changed - stored {byteSize}, actual {actualSize}");
                #endif
                fixed (byte* bytePtr = &Buffer[Position]) {
                    fixed (void* dataPtr = &res[0, 0]) {
                        System.Buffer.MemoryCopy(bytePtr, dataPtr, byteSize, byteSize);
                    }
                }

                Position += byteSize;
            }

            return res;
        }

        [MethodImpl(AggressiveInlining)]
        public T[,,] ReadArray3DUnmanaged<T>() where T : unmanaged {
            if (ReadNullFlag()) return null;

            var dim0 = ReadInt();
            var dim1 = ReadInt();
            var dim2 = ReadInt();
            var byteSize = ReadUint();

            var res = new T[dim0, dim1, dim2];

            if (dim0 == 0 || dim1 == 0 || dim2 == 0) {
                return res;
            }

            unsafe {
                #if DEBUG || FFS_PACK_ENABLE_DEBUG
                var actualSize = (uint) (dim0 * dim1 * dim2 * sizeof(T));
                if (byteSize != actualSize) throw new Exception($"[ReadArray3Unmanaged<{typeof(T)}>] The number of bytes has changed - stored {byteSize}, actual {actualSize}");
                #endif
                fixed (byte* bytePtr = &Buffer[Position]) {
                    fixed (void* dataPtr = &res[0, 0, 0]) {
                        System.Buffer.MemoryCopy(bytePtr, dataPtr, byteSize, byteSize);
                    }
                }

                Position += byteSize;
            }

            return res;
        }
        #endif

        [MethodImpl(AggressiveInlining)]
        public T[] ReadArray<T>() {
            if (ReadNullFlag()) return null;

            var count = ReadInt();
            Position += sizeof(uint); // byteSize
            var res = new T[count];
            for (var i = 0; i < count; i++) {
                res[i] = BinaryPack<T>.Read(ref this);
            }

            return res;
        }
        
        [MethodImpl(AggressiveInlining)]
        public ArraySegment<T> ReadArrayPooled<T>(out ArrayPoolHandle<T> poolHandle) {
            poolHandle = default;
            if (ReadNullFlag()) return default;
            
            var count = ReadInt();
            Position += sizeof(uint); // byteSize
            T[] result;

            if (count > 0) {
                result = ArrayPool<T>.Shared.Rent(count);
                poolHandle = new ArrayPoolHandle<T>(result);
                
                for (var i = 0; i < count; i++) {
                    result[i] = BinaryPack<T>.Read(ref this);
                }
            } else {
                result = Array.Empty<T>();
            }

            return new ArraySegment<T>(result, 0, count);
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadArray<T>(ref T[] result) {
            if (ReadNullFlag()) return;

            var count = ReadInt();
            Position += sizeof(uint); // byteSize
            if (result == null || count > result.Length) {
                result = new T[count];
            }

            for (var i = 0; i < count; i++) {
                result[i] = BinaryPack<T>.Read(ref this);
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadArray<T>(ref T[] result, int idx) {
            if (ReadNullFlag()) return;

            var count = ReadInt();
            Position += sizeof(uint); // byteSize
            if (result == null || count + idx > result.Length) {
                result = new T[count + idx];
            }

            for (var i = 0; i < count; i++) {
                result[i + idx] = BinaryPack<T>.Read(ref this);
            }
        }

        
        #if !FFS_PACK_DISABLE_MULTI_ARRAYS && !UNITY_WEBGL
        [MethodImpl(AggressiveInlining)]
        public T[,] ReadArray2D<T>() {
            if (ReadNullFlag()) return null;

            var dim0 = ReadInt();
            var dim1 = ReadInt();
            Position += sizeof(uint); // byteSize

            var res = new T[dim0, dim1];
            for (var i0 = 0; i0 < dim0; i0++) {
                for (var i1 = 0; i1 < dim1; i1++) {
                    res[i0, i1] = BinaryPack<T>.Read(ref this);
                }
            }

            return res;
        }

        [MethodImpl(AggressiveInlining)]
        public T[,,] ReadArray3D<T>() {
            if (ReadNullFlag()) return null;

            var dim0 = ReadInt();
            var dim1 = ReadInt();
            var dim2 = ReadInt();
            Position += sizeof(uint);

            var res = new T[dim0, dim1, dim2];
            for (var i0 = 0; i0 < dim0; i0++) {
                for (var i1 = 0; i1 < dim1; i1++) {
                    for (var i2 = 0; i2 < dim2; i2++) {
                        res[i0, i1, i2] = BinaryPack<T>.Read(ref this);
                    }
                }
            }

            return res;
        }
        #endif

        [MethodImpl(AggressiveInlining)]
        public void SkipArrayHeaders() {
            if (!ReadNullFlag()) {
                Position += ARRAY_INFO_BYTES;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipArray2DHeaders() {
            if (!ReadNullFlag()) {
                Position += ARRAY2_INFO_BYTES;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipArray3DHeaders() {
            if (!ReadNullFlag()) {
                Position += ARRAY3_INFO_BYTES;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipArray() {
            if (ReadNullFlag()) return;

            Position += sizeof(int); // count
            var byteSize = ReadUint();
            SkipNext(byteSize);
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipArray2D() {
            if (ReadNullFlag()) return;

            Position += sizeof(int); // count 0
            Position += sizeof(int); // count 1
            var byteSize = ReadUint();
            SkipNext(byteSize);
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipArray3D() {
            if (ReadNullFlag()) return;

            Position += sizeof(int); // count 0
            Position += sizeof(int); // count 1
            Position += sizeof(int); // count 2
            var byteSize = ReadUint();
            SkipNext(byteSize);
        }

        [MethodImpl(AggressiveInlining)]
        public List<T> ReadList<T>() {
            if (ReadNullFlag()) return null;

            var count = ReadInt();
            Position += sizeof(int); // byteSize
            var result = new List<T>(count);
            for (var i = 0; i < count; i++) {
                result.Add(BinaryPack<T>.Read(ref this));
            }

            return result;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadList<T>(ref List<T> result) {
            if (ReadNullFlag()) {
                result = null;
            } else {
                var count = ReadInt();
                Position += sizeof(int); // byteSize
                if (result == null) {
                    result = new List<T>(count);
                } else {
                    result.Clear();
                }

                for (var i = 0; i < count; i++) {
                    result.Add(BinaryPack<T>.Read(ref this));
                }
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipListHeaders() {
            SkipArrayHeaders();
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipList() {
            SkipArray();
        }

        [MethodImpl(AggressiveInlining)]
        public Queue<T> ReadQueue<T>() {
            if (ReadNullFlag()) return null;

            var count = ReadInt();
            Position += sizeof(int); // byteSize
            var result = new Queue<T>(count);
            for (var i = 0; i < count; i++) {
                result.Enqueue(BinaryPack<T>.Read(ref this));
            }

            return result;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadQueue<T>(ref Queue<T> result) {
            if (ReadNullFlag()) {
                result = null;
            } else {
                var count = ReadInt();
                Position += sizeof(int); // byteSize
                if (result == null) {
                    result = new Queue<T>(count);
                } else {
                    result.Clear();
                }

                for (var i = 0; i < count; i++) {
                    result.Enqueue(BinaryPack<T>.Read(ref this));
                }
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipQueueHeaders() {
            SkipArrayHeaders();
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipQueue() {
            SkipArray();
        }

        [MethodImpl(AggressiveInlining)]
        public Stack<T> ReadStack<T>() {
            if (ReadNullFlag()) return null;

            var count = ReadInt();
            Position += sizeof(int); // byteSize
            var result = new Stack<T>(count);
            for (var i = 0; i < count; i++) {
                result.Push(BinaryPack<T>.Read(ref this));
            }

            return result;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadStack<T>(ref Stack<T> result) {
            if (ReadNullFlag()) {
                result = null;
            } else {
                var count = ReadInt();
                Position += sizeof(int); // byteSize
                if (result == null) {
                    result = new Stack<T>(count);
                } else {
                    result.Clear();
                }

                for (var i = 0; i < count; i++) {
                    result.Push(BinaryPack<T>.Read(ref this));
                }
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipStackHeaders() {
            SkipArrayHeaders();
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipStack() {
            SkipArray();
        }

        [MethodImpl(AggressiveInlining)]
        public LinkedList<T> ReadLinkedList<T>() {
            if (ReadNullFlag()) return null;

            var count = ReadInt();
            Position += sizeof(int); // byteSize
            var result = new LinkedList<T>();
            for (var i = 0; i < count; i++) {
                result.AddLast(BinaryPack<T>.Read(ref this));
            }

            return result;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadLinkedList<T>(ref LinkedList<T> result) {
            if (ReadNullFlag()) {
                result = null;
            } else {
                var count = ReadInt();
                Position += sizeof(int); // byteSize
                if (result == null) {
                    result = new LinkedList<T>();
                } else {
                    result.Clear();
                }

                for (var i = 0; i < count; i++) {
                    result.AddLast(BinaryPack<T>.Read(ref this));
                }
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipLinkedListHeaders() {
            SkipArrayHeaders();
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipLinkedList() {
            SkipArray();
        }

        [MethodImpl(AggressiveInlining)]
        public HashSet<T> ReadHashSet<T>() {
            if (ReadNullFlag()) return null;

            var count = ReadInt();
            SkipNext(sizeof(int)); // byteSize
            var res = new HashSet<T>(count);
            for (var i = 0; i < count; i++) {
                res.Add(BinaryPack<T>.Read(ref this));
            }

            return res;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadHashSet<T>(ref HashSet<T> result) {
            if (ReadNullFlag()) {
                result = null;
            } else {
                var count = ReadInt();
                SkipNext(sizeof(int)); // byteSize
                if (result == null) {
                    result = new HashSet<T>(count);
                } else {
                    result.Clear();
                }

                for (var i = 0; i < count; i++) {
                    result.Add(BinaryPack<T>.Read(ref this));
                }
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipHashSetHeaders() {
            SkipArrayHeaders();
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipHashSet() {
            SkipArray();
        }

        [MethodImpl(AggressiveInlining)]
        public Dictionary<K, V> ReadDictionary<K, V>() {
            if (ReadNullFlag()) return null;

            var count = ReadInt();
            SkipNext(sizeof(int)); // byteSize
            var result = new Dictionary<K, V>(count);
            for (var i = 0; i < count; i++) {
                var key = BinaryPack<K>.Read(ref this);
                var val = BinaryPack<V>.Read(ref this);
                result[key] = val;
            }

            return result;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadDictionary<K, V>(ref Dictionary<K, V> result) {
            if (ReadNullFlag()) {
                result = null;
            } else {
                var count = ReadInt();
                SkipNext(sizeof(int)); // byteSize
                if (result == null) {
                    result = new Dictionary<K, V>(count);
                } else {
                    result.Clear();
                }

                for (var i = 0; i < count; i++) {
                    var key = BinaryPack<K>.Read(ref this);
                    var val = BinaryPack<V>.Read(ref this);
                    result[key] = val;
                }
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipDictionaryHeaders() {
            SkipArrayHeaders();
        }

        [MethodImpl(AggressiveInlining)]
        public void SkipDictionary() {
            SkipArray();
        }
        #endregion

        #region SPAN
        [MethodImpl(AggressiveInlining)]
        public void ReadBytes(Span<byte> destination) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext((uint) destination.Length)) throw new Exception("ByteReader, Out of bound");
            #endif
            Buffer.AsSpan((int) Position, destination.Length).CopyTo(destination);
            Position += (uint) destination.Length;
        }

        [MethodImpl(AggressiveInlining)]
        public ReadOnlySpan<byte> ReadBytesAsSpan(uint count) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(count)) throw new Exception("ByteReader, Out of bound");
            #endif
            var span = new ReadOnlySpan<byte>(Buffer, (int) Position, (int) count);
            Position += count;
            return span;
        }

        [MethodImpl(AggressiveInlining)]
        public ReadOnlyMemory<byte> ReadBytesAsMemory(uint count) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(count)) throw new Exception("ByteReader, Out of bound");
            #endif
            var memory = new ReadOnlyMemory<byte>(Buffer, (int) Position, (int) count);
            Position += count;
            return memory;
        }

        [MethodImpl(AggressiveInlining)]
        public ReadOnlySpan<byte> RemainingAsSpan() {
            return new ReadOnlySpan<byte>(Buffer, (int) Position, (int) (Size - Position));
        }

        [MethodImpl(AggressiveInlining)]
        public ReadOnlyMemory<byte> RemainingAsMemory() {
            return new ReadOnlyMemory<byte>(Buffer, (int) Position, (int) (Size - Position));
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanaged<T>(Span<T> destination) where T : unmanaged {
            if (destination.Length == 0) return;
            unsafe {
                var size = (uint) (destination.Length * sizeof(T));
                #if DEBUG || FFS_PACK_ENABLE_DEBUG
                if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
                #endif
                fixed (byte* src = &Buffer[Position])
                fixed (T* dest = destination) {
                    System.Buffer.MemoryCopy(src, dest, size, size);
                }
                Position += size;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public int ReadSpanUnmanaged<T>(Span<T> destination) where T : unmanaged {
            if (ReadNullFlag()) return 0;

            var count = ReadInt();
            var byteSize = ReadUint();
            if (count > 0) {
                unsafe {
                    #if DEBUG || FFS_PACK_ENABLE_DEBUG
                    var actualSize = (uint) (count * sizeof(T));
                    if (byteSize != actualSize) throw new Exception($"[ReadSpanUnmanaged<{typeof(T)}>] The number of bytes has changed - stored {byteSize}, actual {actualSize}");
                    if (count > destination.Length) throw new Exception($"[ReadSpanUnmanaged<{typeof(T)}>] Destination too small - need {count}, have {destination.Length}");
                    #endif

                    fixed (byte* src = &Buffer[Position])
                    fixed (T* dest = destination) {
                        System.Buffer.MemoryCopy(src, dest, byteSize, byteSize);
                    }

                    Position += byteSize;
                }
            }

            return count;
        }
        #endregion

        #region UNMANAGED_GENERIC
        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanaged<T1>(out T1 v1)
            where T1 : unmanaged {
            var size = (uint) Unsafe.SizeOf<T1>();
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Buffer[Position]);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanaged<T1, T2>(out T1 v1, out T2 v2)
            where T1 : unmanaged where T2 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanaged<T1, T2, T3>(out T1 v1, out T2 v2, out T3 v3)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanaged<T1, T2, T3, T4>(out T1 v1, out T2 v2, out T3 v3, out T4 v4)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanaged<T1, T2, T3, T4, T5>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanaged<T1, T2, T3, T4, T5, T6>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanaged<T1, T2, T3, T4, T5, T6, T7>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6, out T7 v7)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            v7 = Unsafe.ReadUnaligned<T7>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanaged<T1, T2, T3, T4, T5, T6, T7, T8>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6, out T7 v7, out T8 v8)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>() + Unsafe.SizeOf<T8>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            v7 = Unsafe.ReadUnaligned<T7>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()));
            v8 = Unsafe.ReadUnaligned<T8>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanagedSized<T1>(out T1 v1)
            where T1 : unmanaged {
            var payload = (uint) Unsafe.SizeOf<T1>();
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanagedSized<T1, T2>(out T1 v1, out T2 v2)
            where T1 : unmanaged where T2 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanagedSized<T1, T2, T3>(out T1 v1, out T2 v2, out T3 v3)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanagedSized<T1, T2, T3, T4>(out T1 v1, out T2 v2, out T3 v3, out T4 v4)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanagedSized<T1, T2, T3, T4, T5>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanagedSized<T1, T2, T3, T4, T5, T6>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanagedSized<T1, T2, T3, T4, T5, T6, T7>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6, out T7 v7)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            v7 = Unsafe.ReadUnaligned<T7>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ReadUnmanagedSized<T1, T2, T3, T4, T5, T6, T7, T8>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6, out T7 v7, out T8 v8)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>() + Unsafe.SizeOf<T8>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            v7 = Unsafe.ReadUnaligned<T7>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()));
            v8 = Unsafe.ReadUnaligned<T8>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanaged<T1>(out T1 v1) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T1)} contains references");
            #endif
            var size = (uint) Unsafe.SizeOf<T1>();
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Buffer[Position]);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanaged<T1, T2>(out T1 v1, out T2 v2) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T2)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanaged<T1, T2, T3>(out T1 v1, out T2 v2, out T3 v3) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T3)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanaged<T1, T2, T3, T4>(out T1 v1, out T2 v2, out T3 v3, out T4 v4) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T4)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanaged<T1, T2, T3, T4, T5>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T5)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanaged<T1, T2, T3, T4, T5, T6>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T6)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanaged<T1, T2, T3, T4, T5, T6, T7>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6, out T7 v7) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T6)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T7>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T7)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            v7 = Unsafe.ReadUnaligned<T7>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanaged<T1, T2, T3, T4, T5, T6, T7, T8>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6, out T7 v7, out T8 v8) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T6)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T7>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T7)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T8>()) throw new Exception($"[ForceReadUnmanaged] Type {typeof(T8)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>() + Unsafe.SizeOf<T8>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(size)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            v1 = Unsafe.ReadUnaligned<T1>(ref src);
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            v7 = Unsafe.ReadUnaligned<T7>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()));
            v8 = Unsafe.ReadUnaligned<T8>(ref Unsafe.Add(ref src, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>()));
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanagedSized<T1>(out T1 v1) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T1)} contains references");
            #endif
            var payload = (uint) Unsafe.SizeOf<T1>();
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ForceReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanagedSized<T1, T2>(out T1 v1, out T2 v2) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T2)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ForceReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanagedSized<T1, T2, T3>(out T1 v1, out T2 v2, out T3 v3) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T3)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ForceReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanagedSized<T1, T2, T3, T4>(out T1 v1, out T2 v2, out T3 v3, out T4 v4) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T4)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ForceReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanagedSized<T1, T2, T3, T4, T5>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T5)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ForceReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanagedSized<T1, T2, T3, T4, T5, T6>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T6)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ForceReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanagedSized<T1, T2, T3, T4, T5, T6, T7>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6, out T7 v7) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T6)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T7>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T7)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ForceReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            v7 = Unsafe.ReadUnaligned<T7>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()));
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceReadUnmanagedSized<T1, T2, T3, T4, T5, T6, T7, T8>(out T1 v1, out T2 v2, out T3 v3, out T4 v4, out T5 v5, out T6 v6, out T7 v7, out T8 v8) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T6)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T7>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T7)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T8>()) throw new Exception($"[ForceReadUnmanagedSized] Type {typeof(T8)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>() + Unsafe.SizeOf<T8>());
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (!HasNext(payload + 4)) throw new Exception("ByteReader, Out of bound");
            #endif
            ref var src = ref Buffer[Position];
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            var stored = Unsafe.ReadUnaligned<uint>(ref src);
            if (stored != payload) throw new Exception($"[ForceReadUnmanagedSized] Size mismatch - stored {stored}, expected {payload}");
            #endif
            v1 = Unsafe.ReadUnaligned<T1>(ref Unsafe.Add(ref src, 4));
            v2 = Unsafe.ReadUnaligned<T2>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>()));
            v3 = Unsafe.ReadUnaligned<T3>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()));
            v4 = Unsafe.ReadUnaligned<T4>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()));
            v5 = Unsafe.ReadUnaligned<T5>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()));
            v6 = Unsafe.ReadUnaligned<T6>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()));
            v7 = Unsafe.ReadUnaligned<T7>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()));
            v8 = Unsafe.ReadUnaligned<T8>(ref Unsafe.Add(ref src, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>()));
            Position += payload + 4;
        }
        #endregion
    }
    
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    #endif
    public struct ArrayPoolHandle<T> {
        private T[] _value;
        
        public ArrayPoolHandle(T[] value) {
            _value = value;
        }

        [MethodImpl(AggressiveInlining)]
        public void Return() {
            if (_value != null) {
                ArrayPool<T>.Shared.Return(_value);
                _value = null;
            }
        }
    }
}