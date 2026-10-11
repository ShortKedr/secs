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
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    #endif
    public struct BinaryPackWriter: IDisposable {
        public byte[] Buffer;
        public uint Position;
        public readonly bool Rented;

        public static BinaryPackWriter Create(byte[] buffer, uint position = 0) {
            return new BinaryPackWriter(buffer, position, false);
        }

        public static BinaryPackWriter CreateFromPool(uint minByteSize = 512) {
            return new BinaryPackWriter(ArrayPool<byte>.Shared.Rent((int) minByteSize), 0, true);
        }

        [MethodImpl(AggressiveInlining)]
        private BinaryPackWriter(byte[] buffer, uint position, bool rented) {
            Buffer = buffer;
            Position = position;
            Rented = rented;
        }

        public int CurrentCapacity {
            [MethodImpl(AggressiveInlining)] get => Buffer.Length;
        }

        [MethodImpl(AggressiveInlining)]
        public byte[] CopyToBytes(bool gzip = false) {
            if (gzip) {
                return Gzip(0, Position);
            }

            var bytes = new byte[Position];
            Array.Copy(Buffer, bytes, Position);
            return bytes;
        }

        [MethodImpl(AggressiveInlining)]
        public int CopyToBytes(ref byte[] result, bool gzip = false) {
            if (gzip) {
                return Gzip(ref result, 0, Position);
            }
            
            if (result == null || result.Length < Position) {
                result = new byte[Position];
            }
            
            Array.Copy(Buffer, result, Position);
            return (int) Position;
        }

        [MethodImpl(AggressiveInlining)]
        public BinaryPackReader AsReader() {
            return new BinaryPackReader(Buffer, Position, 0);
        }

        [MethodImpl(AggressiveInlining)]
        public void EnsureSize(uint size) {
            if (Position + size > Buffer.Length) {
                Resize(Position + size);
            }
        }

        [MethodImpl(AggressiveInlining)]
        public uint MakePoint(uint size) {
            var position = Position;
            EnsureSize(size);
            Position += size;
            return position;
        }

        private void Resize(uint size) {
            size--;
            size |= size >> 1;
            size |= size >> 2;
            size |= size >> 4;
            size |= size >> 8;
            size |= size >> 16;
            size++;

            if (Rented) {
                var newBuffer = ArrayPool<byte>.Shared.Rent((int) size);
                Array.Copy(Buffer, newBuffer, Buffer.Length);
                ArrayPool<byte>.Shared.Return(Buffer);
                Buffer = newBuffer;
            } else {
                Array.Resize(ref Buffer, (int) size);
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void Dispose() {
            if (Rented && Buffer != null) {
                ArrayPool<byte>.Shared.Return(Buffer);
            }

            Buffer = null;
        }

        [MethodImpl(AggressiveInlining)]
        public void Skip(uint bytesCount) {
            Position += bytesCount;
        }

        [MethodImpl(AggressiveInlining)]
        public void Skip() {
            Position++;
        }

        [MethodImpl(AggressiveInlining)]
        private void ValidatePosition(uint position, uint size) {
            if (position > Position) throw new Exception($"Position {position} more than current offset {Position}");
            if (position + size > Buffer.Length) throw new Exception($"Position {position} + size {size} more than current capacity {Buffer.Length}");
        }

        [MethodImpl(AggressiveInlining)]
        public bool WriteNotNullFlag(object value) {
            EnsureSize(sizeof(byte));
            if (value == null) {
                Buffer[Position++] = 0;
                return false;
            }

            Buffer[Position++] = 1;
            return true;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteNotNullFlag() {
            EnsureSize(sizeof(byte));
            Buffer[Position++] = 1;
        }

        #region PRIMITIVES
        [MethodImpl(AggressiveInlining)]
        public void WriteByte(byte value) {
            EnsureSize(sizeof(byte));
            Buffer[Position] = value;
            Position += 1;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteByteAt(uint offset, byte value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(byte));
            #endif
            Buffer[offset] = value;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteSbyte(sbyte value) {
            EnsureSize(sizeof(sbyte));
            Buffer[Position] = (byte) value;
            Position += 1;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteSbyteAt(uint offset, sbyte value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(sbyte));
            #endif
            Buffer[offset] = (byte) value;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteBool(bool value) {
            EnsureSize(sizeof(byte));
            Buffer[Position++] = (byte) (value ? 1 : 0);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteBoolAt(uint offset, bool value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(byte));
            #endif
            Buffer[offset] = (byte) (value ? 1 : 0);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteShort(short value) {
            EnsureSize(sizeof(short));
            Buffer[Position] = (byte) value;
            Buffer[Position + 1] = (byte) (value >> 8);
            Position += 2;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteShortAt(uint offset, short value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(short));
            #endif
            Buffer[offset] = (byte) value;
            Buffer[offset + 1] = (byte) (value >> 8);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUshort(ushort value) {
            EnsureSize(sizeof(ushort));
            Buffer[Position] = (byte) value;
            Buffer[Position + 1] = (byte) (value >> 8);
            Position += 2;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUshortAt(uint offset, ushort value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(ushort));
            #endif
            Buffer[offset] = (byte) value;
            Buffer[offset + 1] = (byte) (value >> 8);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteChar(char value) {
            EnsureSize(sizeof(char));
            Buffer[Position] = (byte) value;
            Buffer[Position + 1] = (byte) (value >> 8);
            Position += 2;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteCharAt(uint offset, char value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(char));
            #endif
            Buffer[offset] = (byte) value;
            Buffer[offset + 1] = (byte) (value >> 8);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteInt(int value) {
            EnsureSize(sizeof(int));
            WriteUintAt(Position, (uint) value);
            Position += 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUint(uint value) {
            EnsureSize(sizeof(uint));
            WriteUintAt(Position, value);
            Position += 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUintAt(uint offset, uint value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(uint));
            #endif
            Buffer[offset + 0] = (byte) value;
            Buffer[offset + 1] = (byte) (value >> 8);
            Buffer[offset + 2] = (byte) (value >> 16);
            Buffer[offset + 3] = (byte) (value >> 24);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteVarInt(int value) {
            #if DEBUG
            if (value < 0) throw new Exception($"Value {value} less than 0");
            #endif
            EnsureSize(5);
            if (value < 0x80) {
                Buffer[Position] = (byte) value;
                Position += 1;
            } else if (value < 0x4000) {
                Buffer[Position] = (byte) (value | 0x80);
                Buffer[Position + 1] = (byte) (value >> 7);
                Position += 2;
            } else if (value < 0x200000) {
                Buffer[Position] = (byte) (value | 0x80);
                Buffer[Position + 1] = (byte) ((value >> 7) | 0x80);
                Buffer[Position + 2] = (byte) (value >> 14);
                Position += 3;
            } else if (value < 0x10000000) {
                Buffer[Position] = (byte) (value | 0x80);
                Buffer[Position + 1] = (byte) ((value >> 7) | 0x80);
                Buffer[Position + 2] = (byte) ((value >> 14) | 0x80);
                Buffer[Position + 3] = (byte) (value >> 21);
                Position += 4;
            } else {
                Buffer[Position] = (byte) (value | 0x80);
                Buffer[Position + 1] = (byte) ((value >> 7) | 0x80);
                Buffer[Position + 2] = (byte) ((value >> 14) | 0x80);
                Buffer[Position + 3] = (byte) ((value >> 21) | 0x80);
                Buffer[Position + 4] = (byte) (value >> 28);
                Position += 5;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteVarShort(short value) {
            #if DEBUG
            if (value < 0) throw new Exception($"Value {value} less than 0");
            #endif
            EnsureSize(sizeof(short));

            if (value < 0b10000000) {
                Buffer[Position] = (byte) value;
                Position += 1;
            } else {
                Buffer[Position] = (byte) (value | 0b10000000);
                Buffer[Position + 1] = (byte) (value >> 7);
                Position += 2;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteLong(long value) {
            EnsureSize(sizeof(long));
            WriteUlongAt(Position, (ulong) value);
            Position += 8;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUlong(ulong value) {
            EnsureSize(sizeof(ulong));
            WriteUlongAt(Position, value);
            Position += 8;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUlongAt(uint offset, ulong value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(ulong));
            #endif
            var union = default(Union8);
            union.Ulong = value;
            Buffer[offset + 0] = union.Byte0;
            Buffer[offset + 1] = union.Byte1;
            Buffer[offset + 2] = union.Byte2;
            Buffer[offset + 3] = union.Byte3;
            Buffer[offset + 4] = union.Byte4;
            Buffer[offset + 5] = union.Byte5;
            Buffer[offset + 6] = union.Byte6;
            Buffer[offset + 7] = union.Byte7;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteFloat(float value) {
            EnsureSize(sizeof(float));
            WriteFloatAt(Position, value);
            Position += 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteFloatAt(uint offset, float value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(float));
            #endif
            var union = default(Union4);
            union.Float = value;
            Buffer[offset + 0] = union.Byte0;
            Buffer[offset + 1] = union.Byte1;
            Buffer[offset + 2] = union.Byte2;
            Buffer[offset + 3] = union.Byte3;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteDouble(double value) {
            EnsureSize(sizeof(double));
            WriteDoubleAt(Position, value);
            Position += 8;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteDoubleAt(uint offset, double value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(double));
            #endif
            var union = default(Union8);
            union.Double = value;
            Buffer[offset + 0] = union.Byte0;
            Buffer[offset + 1] = union.Byte1;
            Buffer[offset + 2] = union.Byte2;
            Buffer[offset + 3] = union.Byte3;
            Buffer[offset + 4] = union.Byte4;
            Buffer[offset + 5] = union.Byte5;
            Buffer[offset + 6] = union.Byte6;
            Buffer[offset + 7] = union.Byte7;
        }
        #endregion

        #region BASE_VALUE_TYPES
        [MethodImpl(AggressiveInlining)]
        public void WriteNullable<T>(in T? value) where T : struct {
            if (WriteNotNullFlag(value)) {
                BinaryPack<T>.Write(ref this, value!.Value);
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteDateTime(DateTime value) {
            WriteLong(value.ToUniversalTime().Ticks);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteDateTimeAt(uint offset, DateTime value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(ulong));
            #endif
            WriteUlongAt(offset, (ulong) value.ToUniversalTime().Ticks);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteGuid(in Guid value) {
            EnsureSize(16);
            WriteGuidAt(Position, value);
            Position += 16;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteGuidAt(uint offset, Guid value) {
            #if DEBUG
            ValidatePosition(offset, 16);
            #endif
            unsafe {
                fixed (byte* dest = &Buffer[offset]) {
                    *(Guid*) dest = value;
                }
            }
        }
        #endregion

        #region STRING
        [MethodImpl(AggressiveInlining)]
        public void WriteString32(string value) {
            if (WriteNotNullFlag(value)) {
                EnsureSize((uint) Encoding.UTF8.GetMaxByteCount(value.Length));
                var bytesWritten = Encoding.UTF8.GetBytes(value, 0, value.Length, Buffer, (int) Position + sizeof(int));
                WriteInt(bytesWritten);
                Position += (uint) bytesWritten;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteString16(string value) {
            if (WriteNotNullFlag(value)) {
                var len = Math.Min(value.Length, ushort.MaxValue);
                EnsureSize((uint) Encoding.UTF8.GetMaxByteCount(len));
                var bytesWritten = Encoding.UTF8.GetBytes(value, 0, len, Buffer, (int) Position + sizeof(ushort));
                var bytesLen = (ushort) Math.Min(bytesWritten, ushort.MaxValue);
                WriteUshort(bytesLen);
                Position += bytesLen;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteString8(string value) {
            if (WriteNotNullFlag(value)) {
                var len = Math.Min(value.Length, byte.MaxValue);
                EnsureSize((uint) Encoding.UTF8.GetMaxByteCount(len));
                var bytesWritten = Encoding.UTF8.GetBytes(value, 0, len, Buffer, (int) Position + sizeof(byte));
                var bytesLen = (byte) Math.Min(bytesWritten, byte.MaxValue);
                WriteByte(bytesLen);
                Position += bytesLen;
            }
        }
        #endregion

        #region COLLECTIONS
        [MethodImpl(AggressiveInlining)]
        public void WriteArrayUnmanaged<T>(T[] value) where T : unmanaged {
            WriteArrayUnmanaged(value, 0, value.Length);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteArrayUnmanaged<T>(T[] value, int idx, int count) where T : unmanaged {
            if (WriteNotNullFlag(value)) {
                WriteInt(count);
                var position = MakePoint(sizeof(uint));
                if (count > 0) {
                    unsafe {
                        var size = (uint) (count * sizeof(T));
                        EnsureSize(size);
                        fixed (byte* bytePtr = &Buffer[Position]) {
                            fixed (void* dataPtr = &value[idx]) {
                                System.Buffer.MemoryCopy(dataPtr, bytePtr, size, size);
                            }
                        }

                        Position += size;
                    }
                }
                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }
        
        #if !FFS_PACK_DISABLE_MULTI_ARRAYS && !UNITY_WEBGL
        [MethodImpl(AggressiveInlining)]
        public void WriteArrayUnmanaged<T>(T[,] value) where T : unmanaged {
            if (WriteNotNullFlag(value)) {
                var dim0 = value.GetLength(0);
                var dim1 = value.GetLength(1);

                WriteInt(dim0);
                WriteInt(dim1);
                var position = MakePoint(sizeof(uint));

                if (dim0 != 0 && dim1 != 0) {
                    unsafe {
                        var totalLength = dim0 * dim1;
                        var size = (uint) (totalLength * sizeof(T));
                        EnsureSize(size);

                        fixed (byte* bytePtr = &Buffer[Position])
                        fixed (T* dataPtr = &value[0, 0]) {
                            System.Buffer.MemoryCopy(dataPtr, bytePtr, size, size);
                        }

                        Position += size;
                    }
                }
                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteArrayUnmanaged<T>(T[,,] value) where T : unmanaged {
            if (WriteNotNullFlag(value)) {
                var dim0 = value.GetLength(0);
                var dim1 = value.GetLength(1);
                var dim2 = value.GetLength(2);

                WriteInt(dim0);
                WriteInt(dim1);
                WriteInt(dim2);
                var position = MakePoint(sizeof(uint));

                if (dim0 != 0 && dim1 != 0 && dim2 != 0) {
                    unsafe {
                        var totalLength = dim0 * dim1 * dim2;
                        var size = (uint) (totalLength * sizeof(T));
                        EnsureSize(size);

                        fixed (byte* bytePtr = &Buffer[Position])
                        fixed (T* dataPtr = &value[0, 0, 0]) {
                            System.Buffer.MemoryCopy(dataPtr, bytePtr, size, size);
                        }

                        Position += size;
                    }
                }
                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }
        #endif
        
        [MethodImpl(AggressiveInlining)]
        public void WriteArray<T>(T[] value) {
            WriteArray(value, 0, value.Length);
        }

        public delegate void WriteCollectionDelegate(ref BinaryPackWriter writer, int idx);
        
        [MethodImpl(AggressiveInlining)]
        public void WriteCollection(int idx, int count, WriteCollectionDelegate @delegate) {
            WriteNotNullFlag();
            WriteInt(count);
            var position = MakePoint(sizeof(uint));

            for (var i = idx; i < idx + count; i++) {
                @delegate(ref this, i);
            }

            WriteUintAt(position, Position - (position + sizeof(uint)));
        }
        
        [MethodImpl(AggressiveInlining)]
        public void WriteArray<T>(T[] value, int idx, int count) {
            if (WriteNotNullFlag(value)) {
                WriteInt(count);
                var position = MakePoint(sizeof(uint));

                for (var i = idx; i < idx + count; i++) {
                    BinaryPack<T>.Write(ref this, in value[i]);
                }

                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }

        #if !FFS_PACK_DISABLE_MULTI_ARRAYS && !UNITY_WEBGL
        [MethodImpl(AggressiveInlining)]
        public void WriteArray<T>(T[,] value) {
            if (WriteNotNullFlag(value)) {
                var dim0 = value.GetLength(0);
                var dim1 = value.GetLength(1);

                WriteInt(dim0);
                WriteInt(dim1);
                var position = MakePoint(sizeof(uint));

                for (var i0 = 0; i0 < dim0; i0++) {
                    for (var i1 = 0; i1 < dim1; i1++) {
                        BinaryPack<T>.Write(ref this, in value[i0, i1]);
                    }
                }
                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteArray<T>(T[,,] value) {
            if (WriteNotNullFlag(value)) {
                var dim0 = value.GetLength(0);
                var dim1 = value.GetLength(1);
                var dim2 = value.GetLength(2);

                WriteInt(dim0);
                WriteInt(dim1);
                WriteInt(dim2);
                var position = MakePoint(sizeof(uint));

                for (var i0 = 0; i0 < dim0; i0++) {
                    for (var i1 = 0; i1 < dim1; i1++) {
                        for (var i2 = 0; i2 < dim2; i2++) {
                            BinaryPack<T>.Write(ref this, in value[i0, i1, i2]);
                        }
                    }
                }
                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }
        #endif

        [MethodImpl(AggressiveInlining)]
        public void WriteList<T>(List<T> value, int count = -1) {
            if (WriteNotNullFlag(value)) {
                var len = count >= 0 ? count : value.Count;
                WriteInt(len);
                var position = MakePoint(sizeof(uint));
                for (var i = 0; i < len; i++) {
                    BinaryPack<T>.Write(ref this, value[i]);
                }
                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }
        
        [MethodImpl(AggressiveInlining)]
        public void WriteQueue<T>(Queue<T> value) {
            if (WriteNotNullFlag(value)) {
                WriteInt(value.Count);
                var position = MakePoint(sizeof(uint));
                foreach (var val in value) {
                    BinaryPack<T>.Write(ref this, in val);
                }
                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }
        
        [MethodImpl(AggressiveInlining)]
        public void WriteStack<T>(Stack<T> value) {
            if (WriteNotNullFlag(value)) {
                WriteInt(value.Count);
                var position = MakePoint(sizeof(uint));
                foreach (var val in value) {
                    var value1 = val;
                    BinaryPack<T>.Write(ref this, in value1);
                }
                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }
        
        [MethodImpl(AggressiveInlining)]
        public void WriteLinkedList<T>(LinkedList<T> value) {
            if (WriteNotNullFlag(value)) {
                WriteInt(value.Count);
                var position = MakePoint(sizeof(uint));
                foreach (var val in value) {
                    BinaryPack<T>.Write(ref this, in val);
                }
                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteHashSet<T>(HashSet<T> value) {
            if (WriteNotNullFlag(value)) {
                WriteInt(value.Count);
                var position = MakePoint(sizeof(uint));

                foreach (var val in value) {
                    BinaryPack<T>.Write(ref this, in val);
                }
                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteDictionary<K, V>(Dictionary<K, V> value) {
            if (WriteNotNullFlag(value)) {
                WriteInt(value.Count);
                var position = MakePoint(sizeof(uint));
                foreach (var (key, val) in value) {
                    BinaryPack<K>.Write(ref this, in key);
                    BinaryPack<V>.Write(ref this, in val);
                }
                WriteUintAt(position, Position - (position + sizeof(uint)));
            }
        }
        #endregion

        #region OTHER
        [MethodImpl(AggressiveInlining)]
        public void WriteIntPtr(IntPtr value, uint len) {
            EnsureSize(len);
            unsafe {
                fixed (byte* bytePtr = &Buffer[Position]) {
                    System.Buffer.MemoryCopy(value.ToPointer(), bytePtr, len, len);
                }

                Position += len;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteArraySegment(ArraySegment<byte> value) {
            EnsureSize((uint) value.Count);
            Array.Copy(value.Array!, value.Offset, Buffer, Position, value.Count);
            Position += (uint) value.Count;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteBytes(byte[] value, uint index, uint count) {
            EnsureSize(count);
            Array.Copy(value, index, Buffer, Position, count);
            Position += count;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteBytes(byte[] value) {
            EnsureSize((uint) value.Length);
            Array.Copy(value, 0, Buffer, Position, value.Length);
            Position += (uint) value.Length;
        }
        #endregion

        [MethodImpl(AggressiveInlining)]
        public void WriteFromFile(string filePath, bool gzip = false, uint bufferSize = 4096) {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: (int) bufferSize);
            if (gzip) {
                using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress, false);
                EnsureSize(bufferSize);
                int bytesRead;
                while ((bytesRead = gzipStream.Read(Buffer, (int) Position, (int) bufferSize)) > 0) {
                    Skip((uint) bytesRead);
                    EnsureSize(bufferSize);
                }
            } else {
                var streamLength = fileStream.Length;
                #if DEBUG
                if (streamLength > int.MaxValue) {
                    throw new Exception("Stream length more than " + int.MaxValue);
                }
                #endif
                EnsureSize((uint) streamLength);

                var totalRead = 0;
                while (totalRead < streamLength) {
                    var bytesRead = fileStream.Read(Buffer, (int) Position + totalRead, (int) (streamLength - totalRead));
                    if (bytesRead == 0) {
                        throw new Exception("Unexpected end of file");
                    }

                    totalRead += bytesRead;
                }

                Skip((uint) streamLength);
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void FlushToFile(string filePath, bool gzip = false, bool flushToDisk = false) {
            FlushToFile(filePath, 0, Position, gzip, flushToDisk: flushToDisk);
        }

        [MethodImpl(AggressiveInlining)]
        public void FlushToFile(string filePath, uint offset, uint count, bool gzip = false, int bufferSize = 4096, bool flushToDisk = false, CompressionLevel level = CompressionLevel.Fastest) {
            #if DEBUG
            if (offset + count > Position) {
                throw new Exception("offset + count exceeds buffer length");
            }
            #endif

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }

            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: (int) Math.Min(bufferSize, count));
            if (gzip) {
                using var gzipStream = new GZipStream(fileStream, level, false);
                gzipStream.Write(Buffer, (int) offset, (int) count);
                fileStream.Flush(flushToDisk);
            } else {
                fileStream.Write(Buffer, (int) offset, (int) count);
                fileStream.Flush(flushToDisk);
            }
        }

        [MethodImpl(AggressiveInlining)]
        public int Gzip(ref byte[] result, uint offset, uint count, CompressionLevel level = CompressionLevel.Fastest) {
            #if DEBUG
            if (offset + count > Position) {
                throw new Exception("offset + count exceeds buffer length");
            }
            #endif

            using var outputStream = new MemoryStream((int) count / 2 + 64);
            using (var gzipStream = new GZipStream(outputStream, level, leaveOpen: true)) {
                gzipStream.Write(Buffer, (int) offset, (int) count);
            }

            var length = (int) outputStream.Length;
            if (result == null || result.Length < length) {
                result = new byte[length];
            }
            System.Buffer.BlockCopy(outputStream.GetBuffer(), 0, result, 0, length);
            return length;
        }

        [MethodImpl(AggressiveInlining)]
        public byte[] Gzip(uint offset, uint count, CompressionLevel level = CompressionLevel.Fastest) {
            #if DEBUG
            if (offset + count > Position) {
                throw new Exception("offset + count exceeds buffer length");
            }
            #endif

            using var outputStream = new MemoryStream((int) count / 2 + 64);
            using (var gzipStream = new GZipStream(outputStream, level, leaveOpen: true)) {
                gzipStream.Write(Buffer, (int) offset, (int) count);
            }

            var length = (int) outputStream.Length;
            var result = new byte[length];
            System.Buffer.BlockCopy(outputStream.GetBuffer(), 0, result, 0, length);
            return result;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteGzipData(byte[] data, uint bufferSize = 4096) {
            WriteGzipData(data, 0, data.Length, bufferSize);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteGzipData(byte[] data, int index, int count, uint bufferSize = 4096) {
            using var memoryStream = new MemoryStream(data, index, count);
            using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress, false);
            EnsureSize(bufferSize);
            int bytesRead;
            while ((bytesRead = gzipStream.Read(Buffer, (int) Position, (int) bufferSize)) > 0) {
                Skip((uint) bytesRead);
                EnsureSize(bufferSize);
            }
        }
    }
}