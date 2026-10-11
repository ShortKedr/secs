using System;
using System.Buffers;
using System.Collections.Generic;
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
    public struct BinaryPackReader {
        private const uint ARRAY_INFO_BYTES = sizeof(int) + sizeof(int);      // count + byteSize
        private const uint ARRAY2_INFO_BYTES = sizeof(int) * 2 + sizeof(int); // count x2 + byteSize
        private const uint ARRAY3_INFO_BYTES = sizeof(int) * 3 + sizeof(int); // count x3 + byteSize

        public byte[] Buffer;
        public readonly uint Size;
        public uint Position;

        [MethodImpl(AggressiveInlining)]
        public BinaryPackReader(byte[] buffer, uint size, uint position) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (buffer == null) throw new Exception("buffer is null");
            if (position + size > buffer.Length) throw new Exception("incorrect position or size");
            #endif
            Buffer = buffer;
            Position = position;
            Size = size;
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
            return (short) (Buffer[Position++] | (Buffer[Position++] << 8));
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
            return (ushort) (Buffer[Position++] | (Buffer[Position++] << 8));
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
            return (char) (Buffer[Position++] | (Buffer[Position++] << 8));
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
            var union = default(Union4);
            union.Byte0 = Buffer[Position];
            union.Byte1 = Buffer[Position + 1];
            union.Byte2 = Buffer[Position + 2];
            union.Byte3 = Buffer[Position + 3];
            Position += 4;
            return (int) union.Uint;
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
            var union = default(Union4);
            union.Byte0 = Buffer[Position];
            union.Byte1 = Buffer[Position + 1];
            union.Byte2 = Buffer[Position + 2];
            union.Byte3 = Buffer[Position + 3];
            Position += 4;
            return union.Uint;
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
            return (long) ReadUlong();
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
            var union = default(Union8);
            union.Byte0 = Buffer[Position];
            union.Byte1 = Buffer[Position + 1];
            union.Byte2 = Buffer[Position + 2];
            union.Byte3 = Buffer[Position + 3];
            union.Byte4 = Buffer[Position + 4];
            union.Byte5 = Buffer[Position + 5];
            union.Byte6 = Buffer[Position + 6];
            union.Byte7 = Buffer[Position + 7];
            Position += 8;
            return union.Ulong;
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
            var union = default(Union4);
            union.Byte0 = Buffer[Position];
            union.Byte1 = Buffer[Position + 1];
            union.Byte2 = Buffer[Position + 2];
            union.Byte3 = Buffer[Position + 3];
            Position += 4;
            return union.Float;
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
            var union = default(Union8);
            union.Byte0 = Buffer[Position];
            union.Byte1 = Buffer[Position + 1];
            union.Byte2 = Buffer[Position + 2];
            union.Byte3 = Buffer[Position + 3];
            union.Byte4 = Buffer[Position + 4];
            union.Byte5 = Buffer[Position + 5];
            union.Byte6 = Buffer[Position + 6];
            union.Byte7 = Buffer[Position + 7];
            Position += 8;
            return union.Double;
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