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
            Unsafe.WriteUnaligned(ref Buffer[Position], value);
            Position += 2;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteShortAt(uint offset, short value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(short));
            #endif
            Unsafe.WriteUnaligned(ref Buffer[offset], value);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUshort(ushort value) {
            EnsureSize(sizeof(ushort));
            Unsafe.WriteUnaligned(ref Buffer[Position], value);
            Position += 2;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUshortAt(uint offset, ushort value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(ushort));
            #endif
            Unsafe.WriteUnaligned(ref Buffer[offset], value);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteChar(char value) {
            EnsureSize(sizeof(char));
            Unsafe.WriteUnaligned(ref Buffer[Position], value);
            Position += 2;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteCharAt(uint offset, char value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(char));
            #endif
            Unsafe.WriteUnaligned(ref Buffer[offset], value);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteInt(int value) {
            EnsureSize(sizeof(int));
            Unsafe.WriteUnaligned(ref Buffer[Position], value);
            Position += 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUint(uint value) {
            EnsureSize(sizeof(uint));
            Unsafe.WriteUnaligned(ref Buffer[Position], value);
            Position += 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUintAt(uint offset, uint value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(uint));
            #endif
            Unsafe.WriteUnaligned(ref Buffer[offset], value);
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
            Unsafe.WriteUnaligned(ref Buffer[Position], value);
            Position += 8;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUlong(ulong value) {
            EnsureSize(sizeof(ulong));
            Unsafe.WriteUnaligned(ref Buffer[Position], value);
            Position += 8;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUlongAt(uint offset, ulong value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(ulong));
            #endif
            Unsafe.WriteUnaligned(ref Buffer[offset], value);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteFloat(float value) {
            EnsureSize(sizeof(float));
            Unsafe.WriteUnaligned(ref Buffer[Position], value);
            Position += 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteFloatAt(uint offset, float value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(float));
            #endif
            Unsafe.WriteUnaligned(ref Buffer[offset], value);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteDouble(double value) {
            EnsureSize(sizeof(double));
            Unsafe.WriteUnaligned(ref Buffer[Position], value);
            Position += 8;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteDoubleAt(uint offset, double value) {
            #if DEBUG
            ValidatePosition(offset, sizeof(double));
            #endif
            Unsafe.WriteUnaligned(ref Buffer[offset], value);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteByte(byte v0, byte v1) {
            EnsureSize(2);
            var pos = Position;
            Position += 2;
            WriteByteAt(pos, v0);
            WriteByteAt(pos + 1, v1);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteByte(byte v0, byte v1, byte v2) {
            EnsureSize(3);
            var pos = Position;
            Position += 3;
            WriteByteAt(pos, v0);
            WriteByteAt(pos + 1, v1);
            WriteByteAt(pos + 2, v2);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteByte(byte v0, byte v1, byte v2, byte v3) {
            EnsureSize(4);
            var pos = Position;
            Position += 4;
            WriteByteAt(pos, v0);
            WriteByteAt(pos + 1, v1);
            WriteByteAt(pos + 2, v2);
            WriteByteAt(pos + 3, v3);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteShort(short v0, short v1) {
            EnsureSize(sizeof(short) * 2);
            var pos = Position;
            Position += sizeof(short) * 2;
            WriteShortAt(pos, v0);
            WriteShortAt(pos + 2, v1);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteShort(short v0, short v1, short v2) {
            EnsureSize(sizeof(short) * 3);
            var pos = Position;
            Position += sizeof(short) * 3;
            WriteShortAt(pos, v0);
            WriteShortAt(pos + 2, v1);
            WriteShortAt(pos + 4, v2);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteShort(short v0, short v1, short v2, short v3) {
            EnsureSize(sizeof(short) * 4);
            var pos = Position;
            Position += sizeof(short) * 4;
            WriteShortAt(pos, v0);
            WriteShortAt(pos + 2, v1);
            WriteShortAt(pos + 4, v2);
            WriteShortAt(pos + 6, v3);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUshort(ushort v0, ushort v1) {
            EnsureSize(sizeof(ushort) * 2);
            var pos = Position;
            Position += sizeof(ushort) * 2;
            WriteUshortAt(pos, v0);
            WriteUshortAt(pos + 2, v1);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUshort(ushort v0, ushort v1, ushort v2) {
            EnsureSize(sizeof(ushort) * 3);
            var pos = Position;
            Position += sizeof(ushort) * 3;
            WriteUshortAt(pos, v0);
            WriteUshortAt(pos + 2, v1);
            WriteUshortAt(pos + 4, v2);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUshort(ushort v0, ushort v1, ushort v2, ushort v3) {
            EnsureSize(sizeof(ushort) * 4);
            var pos = Position;
            Position += sizeof(ushort) * 4;
            WriteUshortAt(pos, v0);
            WriteUshortAt(pos + 2, v1);
            WriteUshortAt(pos + 4, v2);
            WriteUshortAt(pos + 6, v3);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteInt(int v0, int v1) {
            EnsureSize(sizeof(int) * 2);
            var pos = Position;
            Position += sizeof(int) * 2;
            WriteUintAt(pos, (uint) v0);
            WriteUintAt(pos + 4, (uint) v1);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteInt(int v0, int v1, int v2) {
            EnsureSize(sizeof(int) * 3);
            var pos = Position;
            Position += sizeof(int) * 3;
            WriteUintAt(pos, (uint) v0);
            WriteUintAt(pos + 4, (uint) v1);
            WriteUintAt(pos + 8, (uint) v2);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteInt(int v0, int v1, int v2, int v3) {
            EnsureSize(sizeof(int) * 4);
            var pos = Position;
            Position += sizeof(int) * 4;
            WriteUintAt(pos, (uint) v0);
            WriteUintAt(pos + 4, (uint) v1);
            WriteUintAt(pos + 8, (uint) v2);
            WriteUintAt(pos + 12, (uint) v3);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUint(uint v0, uint v1) {
            EnsureSize(sizeof(uint) * 2);
            var pos = Position;
            Position += sizeof(uint) * 2;
            WriteUintAt(pos, v0);
            WriteUintAt(pos + 4, v1);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUint(uint v0, uint v1, uint v2) {
            EnsureSize(sizeof(uint) * 3);
            var pos = Position;
            Position += sizeof(uint) * 3;
            WriteUintAt(pos, v0);
            WriteUintAt(pos + 4, v1);
            WriteUintAt(pos + 8, v2);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUint(uint v0, uint v1, uint v2, uint v3) {
            EnsureSize(sizeof(uint) * 4);
            var pos = Position;
            Position += sizeof(uint) * 4;
            WriteUintAt(pos, v0);
            WriteUintAt(pos + 4, v1);
            WriteUintAt(pos + 8, v2);
            WriteUintAt(pos + 12, v3);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteFloat(float v0, float v1) {
            EnsureSize(sizeof(float) * 2);
            var pos = Position;
            Position += sizeof(float) * 2;
            WriteFloatAt(pos, v0);
            WriteFloatAt(pos + 4, v1);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteFloat(float v0, float v1, float v2) {
            EnsureSize(sizeof(float) * 3);
            var pos = Position;
            Position += sizeof(float) * 3;
            WriteFloatAt(pos, v0);
            WriteFloatAt(pos + 4, v1);
            WriteFloatAt(pos + 8, v2);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteFloat(float v0, float v1, float v2, float v3) {
            EnsureSize(sizeof(float) * 4);
            var pos = Position;
            Position += sizeof(float) * 4;
            WriteFloatAt(pos, v0);
            WriteFloatAt(pos + 4, v1);
            WriteFloatAt(pos + 8, v2);
            WriteFloatAt(pos + 12, v3);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteLong(long v0, long v1) {
            EnsureSize(sizeof(long) * 2);
            var pos = Position;
            Position += sizeof(long) * 2;
            WriteUlongAt(pos, (ulong) v0);
            WriteUlongAt(pos + 8, (ulong) v1);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteLong(long v0, long v1, long v2) {
            EnsureSize(sizeof(long) * 3);
            var pos = Position;
            Position += sizeof(long) * 3;
            WriteUlongAt(pos, (ulong) v0);
            WriteUlongAt(pos + 8, (ulong) v1);
            WriteUlongAt(pos + 16, (ulong) v2);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteLong(long v0, long v1, long v2, long v3) {
            EnsureSize(sizeof(long) * 4);
            var pos = Position;
            Position += sizeof(long) * 4;
            WriteUlongAt(pos, (ulong) v0);
            WriteUlongAt(pos + 8, (ulong) v1);
            WriteUlongAt(pos + 16, (ulong) v2);
            WriteUlongAt(pos + 24, (ulong) v3);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUlong(ulong v0, ulong v1) {
            EnsureSize(sizeof(ulong) * 2);
            var pos = Position;
            Position += sizeof(ulong) * 2;
            WriteUlongAt(pos, v0);
            WriteUlongAt(pos + 8, v1);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUlong(ulong v0, ulong v1, ulong v2) {
            EnsureSize(sizeof(ulong) * 3);
            var pos = Position;
            Position += sizeof(ulong) * 3;
            WriteUlongAt(pos, v0);
            WriteUlongAt(pos + 8, v1);
            WriteUlongAt(pos + 16, v2);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUlong(ulong v0, ulong v1, ulong v2, ulong v3) {
            EnsureSize(sizeof(ulong) * 4);
            var pos = Position;
            Position += sizeof(ulong) * 4;
            WriteUlongAt(pos, v0);
            WriteUlongAt(pos + 8, v1);
            WriteUlongAt(pos + 16, v2);
            WriteUlongAt(pos + 24, v3);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteDouble(double v0, double v1) {
            EnsureSize(sizeof(double) * 2);
            var pos = Position;
            Position += sizeof(double) * 2;
            WriteDoubleAt(pos, v0);
            WriteDoubleAt(pos + 8, v1);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteDouble(double v0, double v1, double v2) {
            EnsureSize(sizeof(double) * 3);
            var pos = Position;
            Position += sizeof(double) * 3;
            WriteDoubleAt(pos, v0);
            WriteDoubleAt(pos + 8, v1);
            WriteDoubleAt(pos + 16, v2);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteDouble(double v0, double v1, double v2, double v3) {
            EnsureSize(sizeof(double) * 4);
            var pos = Position;
            Position += sizeof(double) * 4;
            WriteDoubleAt(pos, v0);
            WriteDoubleAt(pos + 8, v1);
            WriteDoubleAt(pos + 16, v2);
            WriteDoubleAt(pos + 24, v3);
        }
        #endregion

        #region BASE_VALUE_TYPES
        [MethodImpl(AggressiveInlining)]
        public void WriteNullable<T>(in T? value) where T : struct {
            if (value.HasValue) {
                EnsureSize(sizeof(byte));
                Buffer[Position++] = 1;
                BinaryPack<T>.Write(ref this, value.Value);
            } else {
                EnsureSize(sizeof(byte));
                Buffer[Position++] = 0;
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
                EnsureSize((uint) Encoding.UTF8.GetMaxByteCount(value.Length) + sizeof(int));
                var bytesWritten = Encoding.UTF8.GetBytes(value, 0, value.Length, Buffer, (int) Position + sizeof(int));
                WriteInt(bytesWritten);
                Position += (uint) bytesWritten;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteString16(string value) {
            if (WriteNotNullFlag(value)) {
                var len = Math.Min(value.Length, ushort.MaxValue);
                EnsureSize((uint) Encoding.UTF8.GetMaxByteCount(len) + sizeof(ushort));
                var bytesWritten = Encoding.UTF8.GetBytes(value, 0, len, Buffer, (int) Position + sizeof(ushort));
                if (bytesWritten > ushort.MaxValue) throw new Exception($"String UTF-8 byte length {bytesWritten} exceeds String16 limit {ushort.MaxValue}");
                WriteUshort((ushort) bytesWritten);
                Position += (uint) bytesWritten;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteString8(string value) {
            if (WriteNotNullFlag(value)) {
                var len = Math.Min(value.Length, byte.MaxValue);
                EnsureSize((uint) Encoding.UTF8.GetMaxByteCount(len) + sizeof(byte));
                var bytesWritten = Encoding.UTF8.GetBytes(value, 0, len, Buffer, (int) Position + sizeof(byte));
                if (bytesWritten > byte.MaxValue) throw new Exception($"String UTF-8 byte length {bytesWritten} exceeds String8 limit {byte.MaxValue}");
                WriteByte((byte) bytesWritten);
                Position += (uint) bytesWritten;
            }
        }
        #endregion

        #region COLLECTIONS
        [MethodImpl(AggressiveInlining)]
        public void WriteArrayUnmanaged<T>(T[] value) where T : unmanaged {
            if (value == null) { WriteNotNullFlag(value); return; }
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
            if (value == null) { WriteNotNullFlag(value); return; }
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

        #region SPAN
        [MethodImpl(AggressiveInlining)]
        public void WriteBytes(ReadOnlySpan<byte> value) {
            var count = (uint) value.Length;
            EnsureSize(count);
            value.CopyTo(Buffer.AsSpan((int) Position, (int) count));
            Position += count;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteBytes(ReadOnlyMemory<byte> value) {
            WriteBytes(value.Span);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteBytes(in ReadOnlySequence<byte> value) {
            var length = (uint) value.Length;
            EnsureSize(length);
            value.CopyTo(Buffer.AsSpan((int) Position, (int) length));
            Position += length;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanaged<T>(ReadOnlySpan<T> value) where T : unmanaged {
            if (value.Length == 0) return;
            unsafe {
                var size = (uint) (value.Length * sizeof(T));
                EnsureSize(size);
                fixed (byte* dest = &Buffer[Position])
                fixed (T* src = value) {
                    System.Buffer.MemoryCopy(src, dest, size, size);
                }
                Position += size;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanaged<T>(ReadOnlyMemory<T> value) where T : unmanaged {
            WriteUnmanaged(value.Span);
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteSpanUnmanaged<T>(ReadOnlySpan<T> value) where T : unmanaged {
            WriteNotNullFlag();
            WriteInt(value.Length);
            var position = MakePoint(sizeof(uint));
            if (value.Length > 0) {
                unsafe {
                    var size = (uint) (value.Length * sizeof(T));
                    EnsureSize(size);
                    fixed (byte* dest = &Buffer[Position])
                    fixed (T* src = value) {
                        System.Buffer.MemoryCopy(src, dest, size, size);
                    }
                    Position += size;
                }
            }
            WriteUintAt(position, Position - (position + sizeof(uint)));
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteSpan<T>(ReadOnlySpan<T> value) {
            WriteNotNullFlag();
            WriteInt(value.Length);
            var position = MakePoint(sizeof(uint));
            for (var i = 0; i < value.Length; i++) {
                BinaryPack<T>.Write(ref this, value[i]);
            }
            WriteUintAt(position, Position - (position + sizeof(uint)));
        }
        #endregion

        #region UNMANAGED_GENERIC

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanaged<T1>(in T1 v1)
            where T1 : unmanaged {
            var size = (uint) Unsafe.SizeOf<T1>();
            EnsureSize(size);
            Unsafe.WriteUnaligned(ref Buffer[Position], v1);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanaged<T1, T2>(in T1 v1, in T2 v2)
            where T1 : unmanaged where T2 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanaged<T1, T2, T3>(in T1 v1, in T2 v2, in T3 v3)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanaged<T1, T2, T3, T4>(in T1 v1, in T2 v2, in T3 v3, in T4 v4)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanaged<T1, T2, T3, T4, T5>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanaged<T1, T2, T3, T4, T5, T6>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanaged<T1, T2, T3, T4, T5, T6, T7>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6, in T7 v7)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()), v7);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanaged<T1, T2, T3, T4, T5, T6, T7, T8>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6, in T7 v7, in T8 v8)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged {
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>() + Unsafe.SizeOf<T8>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()), v7);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>()), v8);
            Position += size;
        }


        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanagedSized<T1>(in T1 v1)
            where T1 : unmanaged {
            var payload = (uint) Unsafe.SizeOf<T1>();
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanagedSized<T1, T2>(in T1 v1, in T2 v2)
            where T1 : unmanaged where T2 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanagedSized<T1, T2, T3>(in T1 v1, in T2 v2, in T3 v3)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanagedSized<T1, T2, T3, T4>(in T1 v1, in T2 v2, in T3 v3, in T4 v4)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanagedSized<T1, T2, T3, T4, T5>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanagedSized<T1, T2, T3, T4, T5, T6>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanagedSized<T1, T2, T3, T4, T5, T6, T7>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6, in T7 v7)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()), v7);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void WriteUnmanagedSized<T1, T2, T3, T4, T5, T6, T7, T8>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6, in T7 v7, in T8 v8)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged {
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>() + Unsafe.SizeOf<T8>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()), v7);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>()), v8);
            Position += payload + 4;
        }


        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanaged<T1>(in T1 v1) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T1)} contains references");
            #endif
            var size = (uint) Unsafe.SizeOf<T1>();
            EnsureSize(size);
            Unsafe.WriteUnaligned(ref Buffer[Position], v1);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanaged<T1, T2>(in T1 v1, in T2 v2) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T2)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanaged<T1, T2, T3>(in T1 v1, in T2 v2, in T3 v3) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T3)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanaged<T1, T2, T3, T4>(in T1 v1, in T2 v2, in T3 v3, in T4 v4) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T4)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanaged<T1, T2, T3, T4, T5>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T5)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanaged<T1, T2, T3, T4, T5, T6>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T6)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanaged<T1, T2, T3, T4, T5, T6, T7>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6, in T7 v7) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T6)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T7>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T7)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()), v7);
            Position += size;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanaged<T1, T2, T3, T4, T5, T6, T7, T8>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6, in T7 v7, in T8 v8) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T6)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T7>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T7)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T8>()) throw new Exception($"[ForceWriteUnmanaged] Type {typeof(T8)} contains references");
            #endif
            var size = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>() + Unsafe.SizeOf<T8>());
            EnsureSize(size);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()), v7);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>()), v8);
            Position += size;
        }


        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanagedSized<T1>(in T1 v1) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T1)} contains references");
            #endif
            var payload = (uint) Unsafe.SizeOf<T1>();
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanagedSized<T1, T2>(in T1 v1, in T2 v2) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T2)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanagedSized<T1, T2, T3>(in T1 v1, in T2 v2, in T3 v3) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T3)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanagedSized<T1, T2, T3, T4>(in T1 v1, in T2 v2, in T3 v3, in T4 v4) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T4)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanagedSized<T1, T2, T3, T4, T5>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T5)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanagedSized<T1, T2, T3, T4, T5, T6>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T6)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanagedSized<T1, T2, T3, T4, T5, T6, T7>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6, in T7 v7) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T6)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T7>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T7)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()), v7);
            Position += payload + 4;
        }

        [MethodImpl(AggressiveInlining)]
        public void ForceWriteUnmanagedSized<T1, T2, T3, T4, T5, T6, T7, T8>(in T1 v1, in T2 v2, in T3 v3, in T4 v4, in T5 v5, in T6 v6, in T7 v7, in T8 v8) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T1>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T1)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T2>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T2)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T3>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T3)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T4>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T4)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T5>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T5)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T6>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T6)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T7>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T7)} contains references");
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T8>()) throw new Exception($"[ForceWriteUnmanagedSized] Type {typeof(T8)} contains references");
            #endif
            var payload = (uint) (Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>() + Unsafe.SizeOf<T8>());
            EnsureSize(payload + 4);
            ref var dst = ref Buffer[Position];
            Unsafe.WriteUnaligned(ref dst, payload);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4), v1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>()), v2);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>()), v3);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>()), v4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>()), v5);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>()), v6);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>()), v7);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, 4 + Unsafe.SizeOf<T1>() + Unsafe.SizeOf<T2>() + Unsafe.SizeOf<T3>() + Unsafe.SizeOf<T4>() + Unsafe.SizeOf<T5>() + Unsafe.SizeOf<T6>() + Unsafe.SizeOf<T7>()), v8);
            Position += payload + 4;
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