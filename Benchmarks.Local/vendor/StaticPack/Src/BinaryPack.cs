using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static System.Runtime.CompilerServices.MethodImplOptions;
#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace FFS.Libraries.StaticPack {
    
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    [Il2CppEagerStaticClassConstruction]
    #endif
    public static class BinaryPack {
        static BinaryPack() {
            RegisterWithCollections(static (ref BinaryPackWriter writer, in sbyte value) => writer.WriteSbyte(value),static (ref BinaryPackReader reader) => reader.ReadSByte(), new UnmanagedPackArrayStrategy<sbyte>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in byte value) => writer.WriteByte(value),static (ref BinaryPackReader reader) => reader.ReadByte(), new UnmanagedPackArrayStrategy<byte>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in short value) => writer.WriteShort(value),static (ref BinaryPackReader reader) => reader.ReadShort(), new UnmanagedPackArrayStrategy<short>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in ushort value) => writer.WriteUshort(value),static (ref BinaryPackReader reader) => reader.ReadUshort(), new UnmanagedPackArrayStrategy<ushort>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in int value) => writer.WriteInt(value),static (ref BinaryPackReader reader) => reader.ReadInt(), new UnmanagedPackArrayStrategy<int>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in uint value) => writer.WriteUint(value),static (ref BinaryPackReader reader) => reader.ReadUint(), new UnmanagedPackArrayStrategy<uint>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in long value) => writer.WriteLong(value),static (ref BinaryPackReader reader) => reader.ReadLong(), new UnmanagedPackArrayStrategy<long>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in ulong value) => writer.WriteUlong(value),static (ref BinaryPackReader reader) => reader.ReadUlong(), new UnmanagedPackArrayStrategy<ulong>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in char value) => writer.WriteChar(value),static (ref BinaryPackReader reader) => reader.ReadChar(), new UnmanagedPackArrayStrategy<char>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in float value) => writer.WriteFloat(value),static (ref BinaryPackReader reader) => reader.ReadFloat(), new UnmanagedPackArrayStrategy<float>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in double value) => writer.WriteDouble(value),static (ref BinaryPackReader reader) => reader.ReadDouble(), new UnmanagedPackArrayStrategy<double>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in bool value) => writer.WriteBool(value),static (ref BinaryPackReader reader) => reader.ReadBool(), new UnmanagedPackArrayStrategy<bool>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in DateTime value) => writer.WriteDateTime(value),static (ref BinaryPackReader reader) => reader.ReadDateTime(), new UnmanagedPackArrayStrategy<DateTime>());
            RegisterWithCollections(static (ref BinaryPackWriter writer, in Guid value) => writer.WriteGuid(in value),static (ref BinaryPackReader reader) => reader.ReadGuid(), new UnmanagedPackArrayStrategy<Guid>());

            RegisterWithCollections(static (ref BinaryPackWriter writer, in string value) => writer.WriteString16(value), static (ref BinaryPackReader reader) => reader.ReadString16(), new ClassPackArrayStrategy<string>());
        }

        public static void Init() {
            // Static constructor
        }

        [MethodImpl(AggressiveInlining)]
        public static void RegisterWithCollections<T, S>(BinaryWriter<T> writer, BinaryReader<T> reader, S strategy = default) where S : IPackArrayStrategy<T> {
            BinaryPack<T>.Register(writer, reader);
            strategy.Register();
            BinaryPack<T[][]>.Register(static (ref BinaryPackWriter writer, in T[][] value) => writer.WriteArray(value), static (ref BinaryPackReader reader) => reader.ReadArray<T[]>());
            BinaryPack<T[][][]>.Register(static (ref BinaryPackWriter writer, in T[][][] value) => writer.WriteArray(value), static (ref BinaryPackReader reader) => reader.ReadArray<T[][]>());
            BinaryPack<List<T>>.Register(static (ref BinaryPackWriter writer, in List<T> value) => writer.WriteList(value), static (ref BinaryPackReader reader) => reader.ReadList<T>());
            BinaryPack<LinkedList<T>>.Register(static (ref BinaryPackWriter writer, in LinkedList<T> value) => writer.WriteLinkedList(value), static (ref BinaryPackReader reader) => reader.ReadLinkedList<T>());
            BinaryPack<Queue<T>>.Register(static (ref BinaryPackWriter writer, in Queue<T> value) => writer.WriteQueue(value), static (ref BinaryPackReader reader) => reader.ReadQueue<T>());
            BinaryPack<Stack<T>>.Register(static (ref BinaryPackWriter writer, in Stack<T> value) => writer.WriteStack(value), static (ref BinaryPackReader reader) => reader.ReadStack<T>());
            BinaryPack<HashSet<T>>.Register(static (ref BinaryPackWriter writer, in HashSet<T> value) => writer.WriteHashSet(value), static (ref BinaryPackReader reader) => reader.ReadHashSet<T>());
        }
        
        [MethodImpl(AggressiveInlining)]
        public static void Register<T>(BinaryWriter<T> writer, BinaryReader<T> reader) {
            BinaryPack<T>.Register(writer, reader);
        }
        
        [MethodImpl(AggressiveInlining)]
        public static bool IsRegistered<T>() {
            return BinaryPack<T>.IsRegistered();
        }

        [MethodImpl(AggressiveInlining)]
        public static T Read<T>(this ref BinaryPackReader reader) => BinaryPack<T>.Read(ref reader);

        [MethodImpl(AggressiveInlining)]
        public static T ReadFromBytes<T>(this byte[] bytes, bool gzip = false, uint byteSizeHint = 4096) {
            return ReadFromBytes<T>(bytes, (uint) bytes.Length, 0, gzip, byteSizeHint);
        }
        
        [MethodImpl(AggressiveInlining)]
        public static T ReadFromBytes<T>(this byte[] bytes, uint size, uint position, bool gzip = false, uint byteSizeHint = 4096) {
            if (gzip) {
                var writer = BinaryPackWriter.CreateFromPool(byteSizeHint);
                writer.WriteGzipData(bytes);
                var reader = writer.AsReader();
                var val =  BinaryPack<T>.Read(ref reader);
                writer.Dispose();
                return val;
            } else {
                var reader = new BinaryPackReader(bytes, size, position);
                return BinaryPack<T>.Read(ref reader);
            }
        }
        
        [MethodImpl(AggressiveInlining)]
        public static T ReadFromFile<T>(this string filePath, bool gzip = false, uint byteSizeHint = 4096) {
            var writer = BinaryPackWriter.CreateFromPool(byteSizeHint);
            writer.WriteFromFile(filePath, gzip);
            var reader = writer.AsReader();
            var val = BinaryPack<T>.Read(ref reader);
            writer.Dispose();
            return val;
        }

        [MethodImpl(AggressiveInlining)]
        public static void Write<T>(this ref BinaryPackWriter writer, in T value) => BinaryPack<T>.Write(ref writer, in value);

        [MethodImpl(AggressiveInlining)]
        public static byte[] WriteToBytes<T>(this T value, uint byteSizeHint = 4096, bool gzip = false) {
            var writer = BinaryPackWriter.CreateFromPool(byteSizeHint);
            BinaryPack<T>.Write(ref writer, in value);
            var result = writer.CopyToBytes(gzip);
            writer.Dispose();
            return result;
        }
        
        [MethodImpl(AggressiveInlining)]
        public static void WriteToBytes<T>(this T value, ref byte[] result, uint byteSizeHint = 4096, bool gzip = false) {
            var writer = BinaryPackWriter.CreateFromPool(byteSizeHint);
            BinaryPack<T>.Write(ref writer, in value);
            writer.CopyToBytes(ref result, gzip);
            writer.Dispose();
        }

        [MethodImpl(AggressiveInlining)]
        public static void WriteToFile<T>(this T value, string filePath, bool gzip = false, bool flushToDisk = false, uint byteSizeHint = 4096) {
            var writer = BinaryPackWriter.CreateFromPool(byteSizeHint);
            BinaryPack<T>.Write(ref writer, in value);
            writer.FlushToFile(filePath, gzip, flushToDisk);
            writer.Dispose();
        }
    }

    public delegate T BinaryReader<T>(ref BinaryPackReader reader);

    public delegate void BinaryWriter<T>(ref BinaryPackWriter writer, in T value);

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    [Il2CppEagerStaticClassConstruction]
    #endif
    internal static class BinaryPack<T> {
        private static BinaryReader<T> _reader;
        private static BinaryWriter<T> _writer;

        [MethodImpl(AggressiveInlining)]
        internal static T Read(ref BinaryPackReader reader) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (_reader == null) {
                throw new Exception($"Reader for type {typeof(T)} not defined");
            }
            #endif
            return _reader(ref reader);
        }

        [MethodImpl(AggressiveInlining)]
        internal static void Write(ref BinaryPackWriter writer, in T value) {
            #if DEBUG || FFS_PACK_ENABLE_DEBUG
            if (_writer == null) {
                throw new Exception($"Writer for type {typeof(T)} not defined");
            }
            #endif
            _writer(ref writer, in value);
        }

        [MethodImpl(AggressiveInlining)]
        internal static bool IsRegistered() {
            return _reader != null && _writer != null;
        }

        [MethodImpl(AggressiveInlining)]
        internal static void Register(BinaryWriter<T> writer, BinaryReader<T> reader) {
            _reader = reader ?? throw new Exception($"Reader for type {typeof(T)} is null");
            _writer = writer ?? throw new Exception($"Writer for type {typeof(T)} is null");
        }
    }
    
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    #endif
    [StructLayout(LayoutKind.Explicit)]
    internal struct Union4 {
        [FieldOffset(0)] public float Float;
        [FieldOffset(0)] public uint Uint;
        [FieldOffset(0)] public byte Byte0;
        [FieldOffset(1)] public byte Byte1;
        [FieldOffset(2)] public byte Byte2;
        [FieldOffset(3)] public byte Byte3;
    }

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    #endif
    [StructLayout(LayoutKind.Explicit)]
    internal struct Union8 {
        [FieldOffset(0)] public double Double;
        [FieldOffset(0)] public ulong Ulong;
        [FieldOffset(0)] public byte Byte0;
        [FieldOffset(1)] public byte Byte1;
        [FieldOffset(2)] public byte Byte2;
        [FieldOffset(3)] public byte Byte3;
        [FieldOffset(4)] public byte Byte4;
        [FieldOffset(5)] public byte Byte5;
        [FieldOffset(6)] public byte Byte6;
        [FieldOffset(7)] public byte Byte7;
    }
}

#if ENABLE_IL2CPP
namespace Unity.IL2CPP.CompilerServices {
    using System;

    internal enum Option {
        NullChecks = 1,
        ArrayBoundsChecks = 2,
        DivideByZeroChecks = 3
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
    internal class Il2CppSetOptionAttribute : Attribute {
        public Option Option { get; }
        public object Value { get; }

        public Il2CppSetOptionAttribute(Option option, object value) {
            Option = option;
            Value = value;
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    internal class Il2CppEagerStaticClassConstructionAttribute : Attribute { }
}
#endif