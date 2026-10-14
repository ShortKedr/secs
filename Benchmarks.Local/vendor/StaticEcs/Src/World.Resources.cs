#if ((DEBUG || FFS_ECS_ENABLE_DEBUG) && !FFS_ECS_DISABLE_DEBUG)
#define FFS_ECS_DEBUG
#endif

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using FFS.Libraries.StaticPack;
using static System.Runtime.CompilerServices.MethodImplOptions;
#if NET5_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif
#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace FFS.Libraries.StaticEcs {
    /// <summary>
    /// Marker interface for types used as world resources
    /// (singleton <see cref="World{TWorld}.Resource{T}"/> and keyed <see cref="World{TWorld}.NamedResource{T}"/>).
    /// <para>
    /// All resource types must implement this interface. This provides an explicit contract
    /// (alongside <see cref="IComponent"/>, <see cref="ITag"/>, <see cref="IEvent"/>),
    /// prevents accidental use of arbitrary types as resources, and allows tooling/editors
    /// to distinguish resources from other types.
    /// </para>
    /// <para>
    /// All four optional methods (<see cref="Guid"/>, <see cref="Version"/>,
    /// <see cref="Write"/>, <see cref="Read"/>) have empty default implementations.
    /// Override <see cref="Guid"/> to opt this resource into automatic serialization
    /// inside <c>WorldSnapshot</c>. Override detection uses reflection at first
    /// <c>SetResource</c> call; non-overridden methods are never invoked.
    /// </para>
    /// </summary>
    public interface IResource {
        /// <summary>
        /// Stable identifier used to recognise this resource type in a <c>WorldSnapshot</c>.
        /// Returning a non-empty <see cref="System.Guid"/> opts the resource into automatic
        /// serialisation. Default returns <c>null</c> — resource is excluded from snapshots.
        /// </summary>
        public Guid? Guid() => null;

        /// <summary>
        /// Schema version stored alongside serialised data.
        /// When the saved version differs from the current one, the framework calls
        /// <see cref="Read"/> for migration instead of bulk byte-copy. Default is <c>0</c>.
        /// </summary>
        public byte Version() => 0;

        /// <summary>
        /// Custom serialisation hook. Required only when <see cref="Guid"/> is set
        /// and the type is not unmanaged (a reference type, or a struct containing references).
        /// For unmanaged types the framework copies raw memory and never invokes this hook unless the version changes.
        /// </summary>
        public void Write(ref BinaryPackWriter writer) {}

        /// <summary>
        /// Custom deserialisation hook. Required only when <see cref="Guid"/> is set
        /// and the type is not unmanaged. Also invoked on version mismatch for unmanaged types.
        /// </summary>
        public void Read(ref BinaryPackReader reader, byte version) {}
    }

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    public abstract partial class World<TWorld> {

        #if ENABLE_IL2CPP
        [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
        #endif
        /// <summary>
        /// Lightweight handle for accessing a singleton resource of type <typeparamref name="T"/>
        /// stored in the world's static <see cref="Resources{TScope,T}"/> storage.
        /// <para>
        /// Resources are globally unique per world per type — there can be at most one instance of
        /// <typeparamref name="T"/> registered at any time. Unlike components (which are per-entity),
        /// resources are world-level singletons used for shared state: configuration, asset caches,
        /// time/delta-time values, input state, or any data that doesn't belong to a specific entity.
        /// </para>
        /// <para>
        /// This struct is a zero-cost handle — it contains no data. All access goes through static
        /// generic storage (<c>Resources&lt;TWorld,T&gt;</c>), so creating or copying this struct is free.
        /// Register the resource via <see cref="SetResource{TResource}(TResource, bool)"/>
        /// before accessing <see cref="Value"/>.
        /// </para>
        /// </summary>
        /// <typeparam name="T">The resource type.</typeparam>
        public readonly struct Resource<T> where T : IResource {
            /// <summary>
            /// Whether a resource of type <typeparamref name="T"/> has been registered in this world.
            /// Returns <c>false</c> before <see cref="SetResource{TResource}(TResource, bool)"/>
            /// or after <see cref="RemoveResource{TResource}"/>.
            /// </summary>
            public bool IsRegistered {
                [MethodImpl(AggressiveInlining)]
                get => Resources<TWorld, T>.IsRegistered;
            }

            /// <summary>
            /// Returns a direct reference to the stored resource value. Modifications via this ref
            /// are written directly to static storage — no setter call required.
            /// <para>
            /// The resource must be registered before accessing this property.
            /// No bounds checking is performed in release builds for maximum performance.
            /// </para>
            /// </summary>
            public ref T Value {
                [MethodImpl(AggressiveInlining)]
                get => ref Resources<TWorld, T>.Value;
            }

            /// <summary>
            /// Read-only counterpart to <see cref="Value"/>. Returns a <c>ref readonly</c> view of the
            /// resource value. 
            /// </summary>
            public ref readonly T ValueRO {
                [MethodImpl(AggressiveInlining)]
                get => ref Resources<TWorld, T>.Value;
            }

            /// <summary>
            /// Sets (or replaces) the resource value. Equivalent to <see cref="SetResource{TResource}(TResource, bool)"/>.
            /// </summary>
            /// <param name="value">The resource value to store.</param>
            /// <param name="clearOnDestroy">
            /// If <c>true</c> (default), the resource is automatically cleared on <see cref="World{TWorld}.Destroy"/>.
            /// Only applied on the first registration — replacing preserves the original setting.
            /// </param>
            [MethodImpl(AggressiveInlining)]
            public void Set(T value, bool clearOnDestroy = true) => Resources<TWorld, T>.Set(value, clearOnDestroy);

            /// <summary>
            /// Removes the resource of type <typeparamref name="T"/> from the world.
            /// Equivalent to <see cref="RemoveResource{TResource}"/>.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public void Remove() => Resources<TWorld, T>.Remove();
        }

        #if ENABLE_IL2CPP
        [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
        #endif
        /// <summary>
        /// Handle for accessing a keyed resource of type <typeparamref name="T"/> identified by a string key.
        /// Unlike <see cref="Resource{T}"/> (one per type), keyed resources allow multiple instances
        /// of the same type distinguished by key — e.g., <c>"player_config"</c> and <c>"enemy_config"</c>
        /// can both be <c>NamedResource&lt;GameConfig&gt;</c>.
        /// <para>
        /// Internally, keyed resources are stored in a shared <c>Dictionary&lt;string, object&gt;</c>
        /// with type-erased <c>Box&lt;T&gt;</c> wrappers. This struct caches the resolved box reference
        /// after the first <see cref="Value"/> access, avoiding repeated dictionary lookups. The cache
        /// is automatically invalidated when the underlying resource is removed or replaced (tracked
        /// via a per-box <c>IsValid</c> flag).
        /// </para>
        /// <para>
        /// Use keyed resources when you need multiple instances of the same type (e.g., per-level config,
        /// named service locators) or when the resource identity is determined at runtime (dynamic keys).
        /// For single-instance-per-type resources, prefer <see cref="Resource{T}"/> which uses
        /// static generic storage with zero lookup cost.
        /// </para>
        /// <para>
        /// <b>Warning:</b> This is a mutable struct that caches an internal reference on first
        /// <see cref="Value"/> access. Do NOT store it in a <c>readonly</c> field or pass by value
        /// after first use — the C# compiler will create a defensive copy, discarding the cache and
        /// causing a dictionary lookup on every access. Store it in a non-readonly field or local variable.
        /// </para>
        /// </summary>
        /// <typeparam name="T">The resource value type.</typeparam>
        public struct NamedResource<T> where T : IResource {
            /// <summary>
            /// The string key identifying this resource in the keyed resource dictionary.
            /// </summary>
            public readonly string Key;
            private NamedResources<TWorld>.BoxBase _cache;

            /// <summary>
            /// Creates a keyed resource handle bound to the specified key.
            /// Does not register the resource — call <see cref="SetResource{TResource}(string, TResource, bool)"/> to register a value.
            /// </summary>
            /// <param name="key">The unique string key for this resource.</param>
            [MethodImpl(AggressiveInlining)]
            public NamedResource(string key) {
                Key = key;
                _cache = null;
            }

            /// <summary>
            /// Whether a resource with this key is currently registered.
            /// Unlike <see cref="Value"/>, this always performs a dictionary lookup (not cached).
            /// </summary>
            public bool IsRegistered {
                [MethodImpl(AggressiveInlining)]
                get => NamedResources<TWorld>.Has(Key);
            }

            /// <summary>
            /// Returns a direct reference to the stored resource value. Modifications via this ref
            /// are written directly to the internal box — no setter call required.
            /// <para>
            /// On the first access (or after cache invalidation), resolves the box from the dictionary
            /// and caches the reference. Subsequent accesses return the cached ref in O(1) without
            /// dictionary lookup. The cache is invalidated automatically when the resource is removed
            /// via <see cref="RemoveResource(string)"/> or world destruction (per-box <c>IsValid</c> flag).
            /// </para>
            /// </summary>
            public ref T Value {
                [MethodImpl(AggressiveInlining)]
                get {
                    if (_cache == null || !_cache.IsValid) {
                        if (!NamedResources<TWorld>.Values.TryGetValue(Key, out var boxObj)) {
                            throw new StaticEcsException($"NamedResource<{typeof(T).Name}> with key '{Key}' not found in World<{typeof(TWorld).Name}>");
                        }
                        _cache = (NamedResources<TWorld>.BoxBase)boxObj;
                    }
                    return ref ((NamedResources<TWorld>.Box<T>)_cache).Value;
                }
            }

            /// <summary>
            /// Read-only counterpart to <see cref="Value"/>. Returns a <c>ref readonly</c> view of the
            /// resource value. 
            /// </summary>
            public ref readonly T ValueRO {
                [MethodImpl(AggressiveInlining)]
                get {
                    if (_cache == null || !_cache.IsValid) {
                        if (!NamedResources<TWorld>.Values.TryGetValue(Key, out var boxObj)) {
                            throw new StaticEcsException($"NamedResource<{typeof(T).Name}> with key '{Key}' not found in World<{typeof(TWorld).Name}>");
                        }
                        _cache = (NamedResources<TWorld>.BoxBase)boxObj;
                    }
                    return ref ((NamedResources<TWorld>.Box<T>)_cache).Value;
                }
            }

            /// <summary>
            /// Sets (or replaces) the value for this resource's <see cref="Key"/>.
            /// Equivalent to <see cref="SetResource{TResource}(string, TResource, bool)"/>.
            /// </summary>
            /// <param name="value">The resource value to store.</param>
            /// <param name="clearOnDestroy">
            /// If <c>true</c> (default), the resource is cleared on <see cref="World{TWorld}.Destroy"/>.
            /// Only applied on the first registration of this key.
            /// </param>
            [MethodImpl(AggressiveInlining)]
            public void Set(T value, bool clearOnDestroy = true) => NamedResources<TWorld>.Set(Key, value, clearOnDestroy);

            /// <summary>
            /// Removes the resource bound to <see cref="Key"/>.
            /// Equivalent to <see cref="RemoveResource(string)"/>.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public void Remove() {
                NamedResources<TWorld>.Remove(Key);
                _cache = null;
            }
        }

        #if ENABLE_IL2CPP
        [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
        [Il2CppEagerStaticClassConstruction]
        #endif
        internal readonly struct Resources<TScope, T> where TScope : struct where T : IResource {
            internal static T Value;
            internal static bool IsRegistered;

            [MethodImpl(AggressiveInlining)]
            internal static bool Has() => IsRegistered;

            [MethodImpl(AggressiveInlining)]
            internal static ref T Get() => ref Value;

            [MethodImpl(AggressiveInlining)]
            internal static void Set(T value, bool clearOnDestroy = true) {
                if (IsRegistered) {
                    Value = value;
                    return;
                }

                IsRegistered = true;
                Value = value;
                ResourcesData<TScope>.Instance.Register(value, clearOnDestroy);
            }

            [MethodImpl(AggressiveInlining)]
            internal static void Remove() {
                var guid = Value.Guid();
                IsRegistered = false;
                Value = default;
                ResourcesData<TScope>.Instance.Unregister<T>(guid);
            }
        }

        #if ENABLE_IL2CPP
        [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
        [Il2CppEagerStaticClassConstruction]
        #endif
        internal readonly struct NamedResources<TScope> where TScope : struct {
            internal abstract class BoxBase {
                internal bool IsValid = true;
                internal abstract IResource GetValue();
                internal abstract void SetValue(IResource value);
            }

            internal class Box<T> : BoxBase where T : IResource {
                public T Value;
                internal override IResource GetValue() => Value;
                internal override void SetValue(IResource value) => Value = (T) value;
            }

            internal static readonly Dictionary<string, object> Values = new();
            internal static readonly HashSet<string> KeysToClear = new();

            [MethodImpl(AggressiveInlining)]
            internal static bool Has(string key) => Values.ContainsKey(key);

            [MethodImpl(AggressiveInlining)]
            internal static ref T Get<T>(string key) where T : IResource {
                if (!Values.TryGetValue(key, out var boxObj)) {
                    throw new StaticEcsException($"NamedResources<{typeof(T).Name}> with key '{key}' not found");
                }

                return ref ((Box<T>)boxObj).Value;
            }

            [MethodImpl(AggressiveInlining)]
            internal static IResource GetRaw(string key) {
                if (!Values.TryGetValue(key, out var boxObj)) {
                    throw new StaticEcsException($"NamedResource with key `{key}` not found in World<{typeof(TWorld).Name}>");
                }
                return ((BoxBase) boxObj).GetValue();
            }

            [MethodImpl(AggressiveInlining)]
            internal static void Clear() {
                foreach (var clearKey in KeysToClear) {
                    if (Values.TryGetValue(clearKey, out var boxObj)) {
                        ((BoxBase) boxObj).IsValid = false;
                    }
                    Values.Remove(clearKey);
                    ResourcesData<TScope>.Instance.UnregisterNamed(clearKey);
                }

                KeysToClear.Clear();
            }

            [MethodImpl(AggressiveInlining)]
            internal static void Set<T>(string key, T value, bool clearOnDestroy = true) where T : IResource {
                if (Values.TryGetValue(key, out var existing)) {
                    ((Box<T>)existing).Value = value;
                    return;
                }

                Values[key] = new Box<T> { Value = value };
                if (clearOnDestroy) {
                    KeysToClear.Add(key);
                }
                ResourcesData<TScope>.Instance.RegisterNamed(key, value);
            }

            [MethodImpl(AggressiveInlining)]
            internal static void SetRaw(string key, IResource value) {
                if (!Values.TryGetValue(key, out var existing)) {
                    throw new StaticEcsException(
                        $"NamedResource with key `{key}` is not registered in World<{typeof(TWorld).Name}>. " +
                        $"Raw SetResource(string, IResource, bool) requires the key to be registered first via typed SetResource<T>(key, value, clearOnDestroy).");
                }
                ((BoxBase) existing).SetValue(value);
            }

            [MethodImpl(AggressiveInlining)]
            internal static void Remove(string key) {
                if (Values.TryGetValue(key, out var boxObj)) {
                    ((BoxBase) boxObj).IsValid = false;
                }
                Values.Remove(key);
                KeysToClear.Remove(key);
                ResourcesData<TScope>.Instance.UnregisterNamed(key);
            }
        }

        #if ENABLE_IL2CPP
        [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
        [Il2CppEagerStaticClassConstruction]
        #endif
        internal struct ResourcesData<TScope> where TScope : struct {
            internal static ResourcesData<TScope> Instance;

            internal Dictionary<Type, Action> ContextClearMethods;
            internal Dictionary<Type, (Func<IResource> get, Action<IResource, bool> set, Action remove)> ValuesGetSetRawMethods;

            internal Dictionary<Guid, ResourceSnapshotEntry> SerializableSingletons;
            internal Dictionary<string, ResourceSnapshotEntry> SerializableNamed;

            internal static void Create() {
                Instance.ContextClearMethods ??= new Dictionary<Type, Action>();
                Instance.ValuesGetSetRawMethods ??= new Dictionary<Type, (Func<IResource>, Action<IResource, bool>, Action)>();
                Instance.SerializableSingletons ??= new Dictionary<Guid, ResourceSnapshotEntry>();
                Instance.SerializableNamed ??= new Dictionary<string, ResourceSnapshotEntry>();
            }

            [MethodImpl(AggressiveInlining)]
            internal void Register<T>(T value, bool clearOnDestroy) where T : IResource {
                var type = typeof(T);
                ValuesGetSetRawMethods[type] = (
                    static () => Resources<TScope, T>.Get(), static (val, clear) => Resources<TScope, T>.Set((T) val, clear), Resources<TScope, T>.Remove
                );
                if (clearOnDestroy) {
                    ContextClearMethods[type] = static () => Resources<TScope, T>.Remove();
                }

                var guid = value.Guid();
                if (guid == null || guid.Value == Guid.Empty) return;
                ResourceTypeMeta<T>.Init<TScope>();
                #if FFS_ECS_DEBUG
                if (SerializableSingletons.ContainsKey(guid.Value)) {
                    throw new StaticEcsException($"Resource with Guid {guid.Value} (type `{typeof(T)}`) is already registered as serializable in scope `{typeof(TScope)}`");
                }
                #endif
                SerializableSingletons[guid.Value] = new ResourceSnapshotEntry {
                    Guid = guid.Value,
                    Version = value.Version(),
                    Unmanaged = ResourceTypeMeta<T>.Unmanaged,
                    Write = static (ref BinaryPackWriter writer, string _) => ResourceSerializer<TScope, T>.WriteSingleton(ref writer),
                    Read = static (ref BinaryPackReader reader, string _, byte version, bool unmanaged) => ResourceSerializer<TScope, T>.ReadSingleton(ref reader, version, unmanaged),
                };
            }

            [MethodImpl(AggressiveInlining)]
            internal void Unregister<T>(Guid? guid) where T : IResource {
                var type = typeof(T);
                ContextClearMethods.Remove(type);
                ValuesGetSetRawMethods.Remove(type);
                if (guid != null && guid.Value != Guid.Empty) {
                    SerializableSingletons.Remove(guid.Value);
                }
            }

            [MethodImpl(AggressiveInlining)]
            internal void RegisterNamed<T>(string key, T value) where T : IResource {
                var guid = value.Guid();
                if (guid == null || guid.Value == Guid.Empty) return;
                ResourceTypeMeta<T>.Init<TScope>();
                SerializableNamed[key] = new ResourceSnapshotEntry {
                    Guid = guid.Value,
                    Version = value.Version(),
                    Unmanaged = ResourceTypeMeta<T>.Unmanaged,
                    Write = static (ref BinaryPackWriter writer, string key) => ResourceSerializer<TScope, T>.WriteNamedResource(ref writer, key),
                    Read = static (ref BinaryPackReader reader, string key, byte version, bool unmanaged) => ResourceSerializer<TScope, T>.ReadNamedResource(ref reader, key, version, unmanaged),
                };
            }

            [MethodImpl(AggressiveInlining)]
            internal void UnregisterNamed(string key) {
                SerializableNamed.Remove(key);
            }

            internal void Clear() {
                if (ContextClearMethods.Count > 0) {
                    var pendingActions = new Action[ContextClearMethods.Count];
                    var actionIndex = 0;
                    foreach (var clearAction in ContextClearMethods.Values) {
                        pendingActions[actionIndex++] = clearAction;
                    }
                    foreach (var clearAction in pendingActions) {
                        clearAction();
                    }
                }
            }

            [MethodImpl(AggressiveInlining)]
            internal readonly ref T GetOrCreate<T>(out bool isNew) where T : IResource, new() {
                isNew = !Resources<TScope, T>.IsRegistered;
                if (isNew) {
                    Resources<TScope, T>.Set(new T());
                }
                return ref Resources<TScope, T>.Value;
            }

            [MethodImpl(AggressiveInlining)]
            internal Dictionary<Type, (Func<IResource>, Action<IResource, bool>, Action)> GetAllGetSetRemoveValuesMethods() {
                return ValuesGetSetRawMethods;
            }

            [MethodImpl(AggressiveInlining)]
            internal readonly bool HasRaw(Type type) => ValuesGetSetRawMethods.ContainsKey(type);

            [MethodImpl(AggressiveInlining)]
            internal readonly IResource GetRaw(Type type) {
                if (!ValuesGetSetRawMethods.TryGetValue(type, out var methods)) {
                    throw new StaticEcsException($"Resource of type `{type}` is not registered in World<{typeof(TWorld).Name}>");
                }
                return methods.get();
            }

            [MethodImpl(AggressiveInlining)]
            internal readonly void RemoveRaw(Type type) {
                if (!ValuesGetSetRawMethods.TryGetValue(type, out var methods)) {
                    throw new StaticEcsException($"Resource of type `{type}` is not registered in World<{typeof(TWorld).Name}>");
                }
                methods.remove();
            }

            [MethodImpl(AggressiveInlining)]
            internal readonly void SetRaw(Type type, IResource value, bool clearOnDestroy) {
                if (!ValuesGetSetRawMethods.TryGetValue(type, out var methods)) {
                    throw new StaticEcsException(
                        $"Resource of type `{type}` is not registered in World<{typeof(TWorld).Name}>. " +
                        $"Raw SetResource requires the type to be registered first via typed SetResource<{type.Name}>(...)");
                }
                methods.set(value, clearOnDestroy);
            }

            internal void WriteSnapshot(ref BinaryPackWriter writer) {
                writer.WriteUshort((ushort)SerializableSingletons.Count);
                foreach (var kvp in SerializableSingletons) {
                    var entry = kvp.Value;
                    writer.WriteGuid(entry.Guid);
                    writer.WriteByte(entry.Version);
                    writer.WriteBool(entry.Unmanaged);
                    var sizePos = writer.MakePoint(sizeof(uint));
                    entry.Write(ref writer, null);
                    writer.WriteUintAt(sizePos, writer.Position - (sizePos + sizeof(uint)));
                }

                writer.WriteUshort((ushort)SerializableNamed.Count);
                foreach (var kvp in SerializableNamed) {
                    var entry = kvp.Value;
                    writer.WriteGuid(entry.Guid);
                    writer.WriteString16(kvp.Key);
                    writer.WriteByte(entry.Version);
                    writer.WriteBool(entry.Unmanaged);
                    var sizePos = writer.MakePoint(sizeof(uint));
                    entry.Write(ref writer, kvp.Key);
                    writer.WriteUintAt(sizePos, writer.Position - (sizePos + sizeof(uint)));
                }
            }

            internal void ReadSnapshot(ref BinaryPackReader reader) {
                var singletonCount = reader.ReadUshort();
                for (var i = 0; i < singletonCount; i++) {
                    var guid = reader.ReadGuid();
                    var version = reader.ReadByte();
                    var unmanaged = reader.ReadBool();
                    var size = reader.ReadUint();
                    if (SerializableSingletons.TryGetValue(guid, out var entry)) {
                        var endPos = reader.Position + size;
                        entry.Read(ref reader, null, version, unmanaged);
                        reader.Position = endPos;
                    } else {
                        reader.SkipNext(size);
                    }
                }

                var namedCount = reader.ReadUshort();
                for (var i = 0; i < namedCount; i++) {
                    var guid = reader.ReadGuid();
                    var key = reader.ReadString16();
                    var version = reader.ReadByte();
                    var unmanaged = reader.ReadBool();
                    var size = reader.ReadUint();
                    if (SerializableNamed.TryGetValue(key, out var entry) && entry.Guid == guid) {
                        var endPos = reader.Position + size;
                        entry.Read(ref reader, key, version, unmanaged);
                        reader.Position = endPos;
                    } else {
                        reader.SkipNext(size);
                    }
                }
            }
        }

        internal delegate void ResourceWriteFn(ref BinaryPackWriter writer, string key);
        internal delegate void ResourceReadFn(ref BinaryPackReader reader, string key, byte savedVersion, bool savedUnmanaged);

        internal struct ResourceSnapshotEntry {
            public Guid Guid;
            public byte Version;
            public bool Unmanaged;
            public ResourceWriteFn Write;
            public ResourceReadFn Read;
        }

        #if ENABLE_IL2CPP
        [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
        [Il2CppEagerStaticClassConstruction]
        #endif
        internal static class ResourceSerializer<TScope, T>
            where TScope : struct
            where T : IResource {

            [MethodImpl(AggressiveInlining)]
            internal static void WriteSingleton(ref BinaryPackWriter writer) {
                if (ResourceTypeMeta<T>.Unmanaged) {
                    writer.ForceWriteUnmanaged(in Resources<TScope, T>.Value);
                } else {
                    Resources<TScope, T>.Value.Write(ref writer);
                }
            }

            [MethodImpl(AggressiveInlining)]
            internal static void ReadSingleton(ref BinaryPackReader reader, byte savedVersion, bool savedUnmanaged) {
                ref var value = ref Resources<TScope, T>.Value;
                var currentVersion = value.Version();
                if (savedUnmanaged && ResourceTypeMeta<T>.Unmanaged && savedVersion == currentVersion) {
                    reader.ForceReadUnmanaged(out value);
                } else {
                    #if FFS_ECS_DEBUG
                    if (!ResourceTypeMeta<T>.HasRead) {
                        throw new StaticEcsException(
                            $"Resource `{typeof(T)}` requires Read method (savedVersion={savedVersion}, currentVersion={currentVersion}, savedUnmanaged={savedUnmanaged}) but it is not implemented");
                    }
                    #endif
                    value.Read(ref reader, savedVersion);
                }
            }

            [MethodImpl(AggressiveInlining)]
            internal static void WriteNamedResource(ref BinaryPackWriter writer, string key) {
                var box = (NamedResources<TScope>.Box<T>)NamedResources<TScope>.Values[key];
                if (ResourceTypeMeta<T>.Unmanaged) {
                    writer.ForceWriteUnmanaged(in box.Value);
                } else {
                    box.Value.Write(ref writer);
                }
            }

            [MethodImpl(AggressiveInlining)]
            internal static void ReadNamedResource(ref BinaryPackReader reader, string key, byte savedVersion, bool savedUnmanaged) {
                var box = (NamedResources<TScope>.Box<T>)NamedResources<TScope>.Values[key];
                var currentVersion = box.Value.Version();
                if (savedUnmanaged && ResourceTypeMeta<T>.Unmanaged && savedVersion == currentVersion) {
                    reader.ForceReadUnmanaged(out box.Value);
                } else {
                    #if FFS_ECS_DEBUG
                    if (!ResourceTypeMeta<T>.HasRead) {
                        throw new StaticEcsException(
                            $"NamedResource `{typeof(T)}` (key '{key}') requires Read method (savedVersion={savedVersion}, currentVersion={currentVersion}, savedUnmanaged={savedUnmanaged}) but it is not implemented");
                    }
                    #endif
                    box.Value.Read(ref reader, savedVersion);
                }
            }
        }
    }

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    [Il2CppEagerStaticClassConstruction]
    #endif
    internal static class ResourceTypeMeta<
        #if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
        #endif
        T> where T : IResource {
        internal static bool Unmanaged;
        internal static bool HasWrite;
        internal static bool HasRead;
        internal static int UnmanagedSize;
        private static bool _initialized;

        private static readonly Type[] WriteParams = { typeof(BinaryPackWriter).MakeByRefType() };
        private static readonly Type[] ReadParams = { typeof(BinaryPackReader).MakeByRefType(), typeof(byte) };

        internal static void Init<TScope>() where TScope : struct {
            if (!_initialized) {
                var t = typeof(T);
                Unmanaged = t.IsValueType && !RuntimeHelpers.IsReferenceOrContainsReferences<T>();
                HasWrite = HasMethod(t, nameof(IResource.Write), WriteParams);
                HasRead = HasMethod(t, nameof(IResource.Read), ReadParams);
                UnmanagedSize = Unmanaged ? BinaryPack.SizeOf<T>() : 0;
                _initialized = true;
            }

            #if FFS_ECS_DEBUG
            if (!Unmanaged && (!HasWrite || !HasRead)) {
                throw new StaticEcsException(
                    $"Resource type `{typeof(T)}` declares Guid for serialization in scope `{typeof(TScope)}` " +
                    $"but is not unmanaged and does not implement both Write and Read. " +
                    $"Either make the type unmanaged (struct without references) or override both Write and Read.");
            }
            #endif
        }

        private static bool HasMethod(
            #if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
            #endif
            Type t, string name, Type[] parameters) {
            return t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public,
                binder: null, types: parameters, modifiers: null) != null;
        }
    }
}
