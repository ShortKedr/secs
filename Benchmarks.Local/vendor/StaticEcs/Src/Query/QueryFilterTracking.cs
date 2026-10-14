#if ((DEBUG || FFS_ECS_ENABLE_DEBUG) && !FFS_ECS_DISABLE_DEBUG)
#define FFS_ECS_DEBUG
#endif

using System.Runtime.CompilerServices;
using static System.Runtime.CompilerServices.MethodImplOptions;
#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif
#if NET5_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value

namespace FFS.Libraries.StaticEcs {

    #region ALL_ADDED
    /// <summary>
    /// Query filter that matches entities which had the specified component types added since the system's last tick.
    /// Uses AddedHeuristicChunks/AddedMask for filtering.
    /// <para>
    /// Available with 1-5 type parameters. Requires each type parameter to implement <see cref="ITrackableAdded"/>.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requires the component type to implement <see cref="ITrackableAdded"/>.
    /// Combine with <c>All&lt;T&gt;</c> to filter entities that were added AND currently have the component:
    /// <c>world.Query&lt;All&lt;Position&gt;, AllAdded&lt;Position&gt;&gt;()</c>.
    /// </para>
    /// <para>
    /// Tracking is managed automatically by `W.Tick()`. Use the `fromTick` constructor parameter for custom tick ranges.
    /// Added and Deleted masks are independent — an entity can have both bits set if a component was
    /// added and deleted (or deleted and re-added) between ticks.
    /// </para>
    /// </remarks>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllAdded<T0> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public AllAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllAdded{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllAdded<T0, T1> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public AllAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllAdded{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllAdded<T0, T1, T2> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded
        where T2 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public AllAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T2>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.AddedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T2>.Instance.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T2>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T2>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllAdded{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllAdded<T0, T1, T2, T3> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded
        where T2 : struct, IComponentOrTag, ITrackableAdded
        where T3 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public AllAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T2>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T3>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.AddedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T2>.Instance.AddedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T3>.Instance.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T2>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T3>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T2>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T3>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllAdded{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllAdded<T0, T1, T2, T3, T4> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded
        where T2 : struct, IComponentOrTag, ITrackableAdded
        where T3 : struct, IComponentOrTag, ITrackableAdded
        where T4 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public AllAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T2>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T3>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T4>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.AddedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T2>.Instance.AddedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T3>.Instance.AddedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T4>.Instance.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T2>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T3>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T4>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T2>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T3>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T4>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }
    #endregion
    #region NONE_ADDED
    /// <summary>
    /// Negative query filter that excludes entities which had the specified component types added since the system's last tick.
    /// Uses inverted AddedMask for entity-level filtering. Chunk-level filtering is a no-op (cannot efficiently invert at chunk level).
    /// <para>
    /// Available with 1-5 type parameters. Requires each type parameter to implement <see cref="ITrackableAdded"/>.
    /// </para>
    /// </summary>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneAdded<T0> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public NoneAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return ~World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneAdded{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneAdded<T0, T1> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public NoneAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneAdded{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneAdded<T0, T1, T2> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded
        where T2 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public NoneAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T2>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneAdded{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneAdded<T0, T1, T2, T3> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded
        where T2 : struct, IComponentOrTag, ITrackableAdded
        where T3 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public NoneAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T2>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T3>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneAdded{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneAdded<T0, T1, T2, T3, T4> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded
        where T2 : struct, IComponentOrTag, ITrackableAdded
        where T3 : struct, IComponentOrTag, ITrackableAdded
        where T4 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public NoneAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T4>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T2>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T3>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T4>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }
    #endregion
    #region ANY_ADDED
    /// <summary>
    /// Query filter that matches entities which had at least one of the specified component types added since the system's last tick.
    /// Uses AddedHeuristicChunks/AddedMask for filtering with OR logic.
    /// <para>
    /// Available with 2-5 type parameters. Requires each type parameter to implement <see cref="ITrackableAdded"/>.
    /// </para>
    /// </summary>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyAdded<T0, T1> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public AnyAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AnyAdded{T0, T1}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyAdded<T0, T1, T2> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded
        where T2 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public AnyAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T2>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.AddedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T2>.Instance.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T2>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AnyAdded{T0, T1}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyAdded<T0, T1, T2, T3> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded
        where T2 : struct, IComponentOrTag, ITrackableAdded
        where T3 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public AnyAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T2>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T3>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.AddedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T2>.Instance.AddedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T3>.Instance.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T2>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T3>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AnyAdded{T0, T1}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyAdded<T0, T1, T2, T3, T4> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableAdded
        where T1 : struct, IComponentOrTag, ITrackableAdded
        where T2 : struct, IComponentOrTag, ITrackableAdded
        where T3 : struct, IComponentOrTag, ITrackableAdded
        where T4 : struct, IComponentOrTag, ITrackableAdded {

        public readonly ulong FromTick;
        public AnyAdded(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T2>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T3>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T4>.Instance.AddedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.AddedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T2>.Instance.AddedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T3>.Instance.AddedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T4>.Instance.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T4>.Instance.AddedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T2>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T3>.Instance.AddedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T4>.Instance.AddedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.AddedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.AddedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.AddedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }
    #endregion
    #region ALL_DELETED
    /// <summary>
    /// Query filter that matches entities which had the specified component types deleted since the system's last tick.
    /// Uses DeletedHeuristicChunks/DeletedMask for filtering.
    /// <para>
    /// Available with 1-5 type parameters. Requires each type parameter to implement <see cref="ITrackableDeleted"/>.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requires the component type to implement <see cref="ITrackableDeleted"/>.
    /// Note that matched entities may no longer have the component — the mask records the deletion event,
    /// not the current presence. Combine with <c>All&lt;T&gt;</c> if you need entities that still have the component.
    /// </para>
    /// <para>
    /// Tracking is managed automatically by `W.Tick()`. Use the `fromTick` constructor parameter for custom tick ranges.
    /// Added and Deleted masks are independent — an entity can have both bits set if a component was
    /// added and deleted (or deleted and re-added) between ticks.
    /// </para>
    /// </remarks>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllDeleted<T0> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public AllDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllDeleted{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllDeleted<T0, T1> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public AllDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllDeleted{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllDeleted<T0, T1, T2> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted
        where T2 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public AllDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T2>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.DeletedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T2>.Instance.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T2>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T2>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllDeleted{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllDeleted<T0, T1, T2, T3> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted
        where T2 : struct, IComponentOrTag, ITrackableDeleted
        where T3 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public AllDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T2>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T3>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.DeletedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T2>.Instance.DeletedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T3>.Instance.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T2>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T3>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T2>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T3>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllDeleted{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllDeleted<T0, T1, T2, T3, T4> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted
        where T2 : struct, IComponentOrTag, ITrackableDeleted
        where T3 : struct, IComponentOrTag, ITrackableDeleted
        where T4 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public AllDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T2>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T3>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T4>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.DeletedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T2>.Instance.DeletedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T3>.Instance.DeletedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T4>.Instance.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T2>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T3>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T4>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T2>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T3>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T4>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }
    #endregion
    #region NONE_DELETED
    /// <summary>
    /// Negative query filter that excludes entities which had the specified component types deleted since the system's last tick.
    /// Uses inverted DeletedMask for entity-level filtering. Chunk-level filtering is a no-op (cannot efficiently invert at chunk level).
    /// <para>
    /// Available with 1-5 type parameters. Requires each type parameter to implement <see cref="ITrackableDeleted"/>.
    /// </para>
    /// </summary>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneDeleted<T0> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public NoneDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return ~World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneDeleted{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneDeleted<T0, T1> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public NoneDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneDeleted{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneDeleted<T0, T1, T2> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted
        where T2 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public NoneDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T2>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneDeleted{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneDeleted<T0, T1, T2, T3> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted
        where T2 : struct, IComponentOrTag, ITrackableDeleted
        where T3 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public NoneDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T2>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T3>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneDeleted{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneDeleted<T0, T1, T2, T3, T4> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted
        where T2 : struct, IComponentOrTag, ITrackableDeleted
        where T3 : struct, IComponentOrTag, ITrackableDeleted
        where T4 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public NoneDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T4>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T2>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T3>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T4>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }
    #endregion
    #region ANY_DELETED
    /// <summary>
    /// Query filter that matches entities which had at least one of the specified component types deleted since the system's last tick.
    /// Uses DeletedHeuristicChunks/DeletedMask for filtering with OR logic.
    /// <para>
    /// Available with 2-5 type parameters. Requires each type parameter to implement <see cref="ITrackableDeleted"/>.
    /// </para>
    /// </summary>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyDeleted<T0, T1> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public AnyDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AnyDeleted{T0, T1}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyDeleted<T0, T1, T2> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted
        where T2 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public AnyDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T2>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.DeletedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T2>.Instance.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T2>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AnyDeleted{T0, T1}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyDeleted<T0, T1, T2, T3> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted
        where T2 : struct, IComponentOrTag, ITrackableDeleted
        where T3 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public AnyDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T2>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T3>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.DeletedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T2>.Instance.DeletedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T3>.Instance.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T2>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T3>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AnyDeleted{T0, T1}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyDeleted<T0, T1, T2, T3, T4> : IQueryFilter
        where T0 : struct, IComponentOrTag, ITrackableDeleted
        where T1 : struct, IComponentOrTag, ITrackableDeleted
        where T2 : struct, IComponentOrTag, ITrackableDeleted
        where T3 : struct, IComponentOrTag, ITrackableDeleted
        where T4 : struct, IComponentOrTag, ITrackableDeleted {

        public readonly ulong FromTick;
        public AnyDeleted(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T2>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T3>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T4>.Instance.DeletedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.DeletedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T2>.Instance.DeletedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T3>.Instance.DeletedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T4>.Instance.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T4>.Instance.DeletedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T2>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T3>.Instance.DeletedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T4>.Instance.DeletedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.DeletedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.DeletedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.DeletedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }
    #endregion
    #region CREATED
    /// <summary>
    /// Query filter that matches entities which were created since the system's last tick.
    /// <para>
    /// Requires <c>WorldConfig.TrackCreated</c> to be <c>true</c>.
    /// Tracking is managed automatically by `W.Tick()`. Use the `fromTick` constructor parameter for custom tick ranges.
    /// </para>
    /// </summary>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct Created : IQueryFilter {

        public readonly ulong FromTick;
        public Created(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            #if FFS_ECS_DEBUG
            World<TWorld>.AssertTrackCreated();
            #endif
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return data.CreatedHeuristicHistory(from, data.CurrentTick, chunkIdx);
            }
            return data.CreatedTrackingChunks[chunkIdx];
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            #if FFS_ECS_DEBUG
            World<TWorld>.AssertTrackCreated();
            #endif
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return data.CreatedMaskHistory(from, data.CurrentTick, segmentIdx, segmentBlockIdx);
            }
            return data.CreatedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif

        public ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType => throw new System.NotImplementedException();

        public ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType => throw new System.NotImplementedException();
    }
    #endregion

    #if !FFS_ECS_DISABLE_CHANGED_TRACKING

    #region ALL_CHANGED
    /// <summary>
    /// Query filter that matches entities which had the specified component types changed since the system's last tick.
    /// Uses ChangedHeuristicChunks/ChangedMask for filtering.
    /// <para>
    /// Available with 1-5 type parameters. Requires each type parameter to implement <see cref="ITrackableChanged"/>.
    /// </para>
    /// </summary>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllChanged<T0> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public AllChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllChanged{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllChanged<T0, T1> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public AllChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllChanged{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllChanged<T0, T1, T2> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged
        where T2 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public AllChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T2>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.ChangedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T2>.Instance.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T2>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T2>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllChanged{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllChanged<T0, T1, T2, T3> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged
        where T2 : struct, IComponent, ITrackableChanged
        where T3 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public AllChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T2>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T3>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.ChangedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T2>.Instance.ChangedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T3>.Instance.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T2>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T3>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T2>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T3>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AllChanged{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AllChanged<T0, T1, T2, T3, T4> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged
        where T2 : struct, IComponent, ITrackableChanged
        where T3 : struct, IComponent, ITrackableChanged
        where T4 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public AllChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T1>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T2>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T3>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       & World<TWorld>.Components<T4>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T1>.Instance.ChangedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T2>.Instance.ChangedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T3>.Instance.ChangedChunkMask(chunkIdx)
                   & World<TWorld>.Components<T4>.Instance.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T2>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T3>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       & World<TWorld>.Components<T4>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T2>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T3>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & World<TWorld>.Components<T4>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   & BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }
    #endregion

    #region NONE_CHANGED
    /// <summary>
    /// Negative query filter that excludes entities which had the specified component types changed since the system's last tick.
    /// Uses inverted ChangedMask for entity-level filtering. Chunk-level filtering is a no-op (cannot efficiently invert at chunk level).
    /// <para>
    /// Available with 1-5 type parameters. Requires each type parameter to implement <see cref="ITrackableChanged"/>.
    /// </para>
    /// </summary>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneChanged<T0> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public NoneChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return ~World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneChanged{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneChanged<T0, T1> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public NoneChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneChanged{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneChanged<T0, T1, T2> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged
        where T2 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public NoneChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T2>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneChanged{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneChanged<T0, T1, T2, T3> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged
        where T2 : struct, IComponent, ITrackableChanged
        where T3 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public NoneChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T2>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T3>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="NoneChanged{T0}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct NoneChanged<T0, T1, T2, T3, T4> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged
        where T2 : struct, IComponent, ITrackableChanged
        where T3 : struct, IComponent, ITrackableChanged
        where T4 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public NoneChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return ~(World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T4>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx));
            }
            return ~World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T2>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T3>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~World<TWorld>.Components<T4>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return ulong.MaxValue;
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return ~BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   & ~BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }
    #endregion

    #region ANY_CHANGED
    /// <summary>
    /// Query filter that matches entities which had at least one of the specified component types changed since the system's last tick.
    /// Uses ChangedHeuristicChunks/ChangedMask for filtering with OR logic.
    /// <para>
    /// Available with 2-5 type parameters. Requires each type parameter to implement <see cref="ITrackableChanged"/>.
    /// </para>
    /// </summary>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyChanged<T0, T1> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public AnyChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AnyChanged{T0, T1}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyChanged<T0, T1, T2> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged
        where T2 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public AnyChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T2>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.ChangedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T2>.Instance.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T2>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AnyChanged{T0, T1}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyChanged<T0, T1, T2, T3> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged
        where T2 : struct, IComponent, ITrackableChanged
        where T3 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public AnyChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T2>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T3>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.ChangedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T2>.Instance.ChangedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T3>.Instance.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T2>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T3>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }

    /// <inheritdoc cref="AnyChanged{T0, T1}"/>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    public readonly struct AnyChanged<T0, T1, T2, T3, T4> : IQueryFilter
        where T0 : struct, IComponent, ITrackableChanged
        where T1 : struct, IComponent, ITrackableChanged
        where T2 : struct, IComponent, ITrackableChanged
        where T3 : struct, IComponent, ITrackableChanged
        where T4 : struct, IComponent, ITrackableChanged {

        public readonly ulong FromTick;
        public AnyChanged(ulong fromTick = 0) { FromTick = fromTick; }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T2>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T3>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx)
                       | World<TWorld>.Components<T4>.Instance.ChangedHeuristicHistory(from, data.CurrentTick, data.TrackingBufferSize, chunkIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T1>.Instance.ChangedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T2>.Instance.ChangedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T3>.Instance.ChangedChunkMask(chunkIdx)
                   | World<TWorld>.Components<T4>.Instance.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            ref var data = ref World<TWorld>.Data.Instance;
            if (data.TrackingBufferSize > 0) {
                var from = FromTick != 0 ? FromTick : data.CurrentLastTick;
                return World<TWorld>.Components<T0>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T1>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T2>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T3>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx)
                       | World<TWorld>.Components<T4>.Instance.ChangedMaskHistory(from, data.CurrentTick, data.TrackingBufferSize, segmentIdx, segmentBlockIdx);
            }
            return World<TWorld>.Components<T0>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T1>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T2>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T3>.Instance.ChangedMask(segmentIdx, segmentBlockIdx)
                   | World<TWorld>.Components<T4>.Instance.ChangedMask(segmentIdx, segmentBlockIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.ChangedChunkMask(chunkIdx)
                   | BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.ChangedChunkMask(chunkIdx);
        }

        [MethodImpl(AggressiveInlining)]
        public unsafe ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType {
            return BurstView<TWorld>.ComponentMasks<T0>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T1>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T2>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T3>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx)
                   | BurstView<TWorld>.ComponentMasks<T4>.SharedValue.Data.ChangedMask(segmentIdx, segmentBlockIdx);
        }
        #endif

        #if FFS_ECS_DEBUG
        [MethodImpl(AggressiveInlining)]
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType { }
        #endif
    }
    #endregion

    #region TRACKER
    internal interface IChangedTracker<TWorld> where TWorld : struct, IWorldType {
        bool IsActive { get; }

        void ApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx);
        
        #if FFS_ECS_BURST
        void BurstApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx);
        #endif
    }

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    internal readonly struct ChangedTracker<TWorld, T0> : IChangedTracker<TWorld> where TWorld : struct, IWorldType
        where T0 : struct, IComponent {
        private readonly bool _track0;

        public bool IsActive {
            [MethodImpl(AggressiveInlining)] get => _track0;
        }

        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(byte _) {
            _track0 = World<TWorld>.Components<T0>.Instance.TrackChanged;
        }

        [MethodImpl(AggressiveInlining)]
        public void ApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) World<TWorld>.Components<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(bool _) {
            _track0 = BurstView<TWorld>.ComponentMasks<T0>.Instance.TrackChanged;
        }
        
        [MethodImpl(AggressiveInlining)]
        public void BurstApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) BurstView<TWorld>.ComponentMasks<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }
        #endif
    }

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    internal readonly struct ChangedTracker<TWorld, T0, T1> : IChangedTracker<TWorld> where TWorld : struct, IWorldType
        where T0 : struct, IComponent
        where T1 : struct, IComponent {
        private readonly bool _track0;
        private readonly bool _track1;

        public bool IsActive {
            [MethodImpl(AggressiveInlining)] get => _track0 || _track1;
        }

        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(byte _) {
            _track0 = World<TWorld>.Components<T0>.Instance.TrackChanged;
            _track1 = World<TWorld>.Components<T1>.Instance.TrackChanged;
        }

        [MethodImpl(AggressiveInlining)]
        public void ApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) World<TWorld>.Components<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track1) World<TWorld>.Components<T1>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(bool _) {
            _track0 = BurstView<TWorld>.ComponentMasks<T0>.Instance.TrackChanged;
            _track1 = BurstView<TWorld>.ComponentMasks<T1>.Instance.TrackChanged;
        }
        
        [MethodImpl(AggressiveInlining)]
        public void BurstApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) BurstView<TWorld>.ComponentMasks<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track1) BurstView<TWorld>.ComponentMasks<T1>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }
        #endif
    }

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    internal readonly struct ChangedTracker<TWorld, T0, T1, T2> : IChangedTracker<TWorld> where TWorld : struct, IWorldType
        where T0 : struct, IComponent
        where T1 : struct, IComponent
        where T2 : struct, IComponent {
        private readonly bool _track0;
        private readonly bool _track1;
        private readonly bool _track2;

        public bool IsActive {
            [MethodImpl(AggressiveInlining)] get => _track0 || _track1 || _track2;
        }

        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(byte _) {
            _track0 = World<TWorld>.Components<T0>.Instance.TrackChanged;
            _track1 = World<TWorld>.Components<T1>.Instance.TrackChanged;
            _track2 = World<TWorld>.Components<T2>.Instance.TrackChanged;
        }

        [MethodImpl(AggressiveInlining)]
        public void ApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) World<TWorld>.Components<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track1) World<TWorld>.Components<T1>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track2) World<TWorld>.Components<T2>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(bool _) {
            _track0 = BurstView<TWorld>.ComponentMasks<T0>.Instance.TrackChanged;
            _track1 = BurstView<TWorld>.ComponentMasks<T1>.Instance.TrackChanged;
            _track2 = BurstView<TWorld>.ComponentMasks<T2>.Instance.TrackChanged;
        }
        
        [MethodImpl(AggressiveInlining)]
        public void BurstApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) BurstView<TWorld>.ComponentMasks<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track1) BurstView<TWorld>.ComponentMasks<T1>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track2) BurstView<TWorld>.ComponentMasks<T2>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }
        #endif
    }

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    internal readonly struct ChangedTracker<TWorld, T0, T1, T2, T3> : IChangedTracker<TWorld> where TWorld : struct, IWorldType
        where T0 : struct, IComponent
        where T1 : struct, IComponent
        where T2 : struct, IComponent
        where T3 : struct, IComponent {
        private readonly bool _track0;
        private readonly bool _track1;
        private readonly bool _track2;
        private readonly bool _track3;

        public bool IsActive {
            [MethodImpl(AggressiveInlining)] get => _track0 || _track1 || _track2 || _track3;
        }

        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(byte _) {
            _track0 = World<TWorld>.Components<T0>.Instance.TrackChanged;
            _track1 = World<TWorld>.Components<T1>.Instance.TrackChanged;
            _track2 = World<TWorld>.Components<T2>.Instance.TrackChanged;
            _track3 = World<TWorld>.Components<T3>.Instance.TrackChanged;
        }

        [MethodImpl(AggressiveInlining)]
        public void ApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) World<TWorld>.Components<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track1) World<TWorld>.Components<T1>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track2) World<TWorld>.Components<T2>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track3) World<TWorld>.Components<T3>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(bool _) {
            _track0 = BurstView<TWorld>.ComponentMasks<T0>.Instance.TrackChanged;
            _track1 = BurstView<TWorld>.ComponentMasks<T1>.Instance.TrackChanged;
            _track2 = BurstView<TWorld>.ComponentMasks<T2>.Instance.TrackChanged;
            _track3 = BurstView<TWorld>.ComponentMasks<T3>.Instance.TrackChanged;
        }
        
        [MethodImpl(AggressiveInlining)]
        public void BurstApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) BurstView<TWorld>.ComponentMasks<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track1) BurstView<TWorld>.ComponentMasks<T1>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track2) BurstView<TWorld>.ComponentMasks<T2>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track3) BurstView<TWorld>.ComponentMasks<T3>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }
        #endif
    }

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    internal readonly struct ChangedTracker<TWorld, T0, T1, T2, T3, T4> : IChangedTracker<TWorld> where TWorld : struct, IWorldType
        where T0 : struct, IComponent
        where T1 : struct, IComponent
        where T2 : struct, IComponent
        where T3 : struct, IComponent
        where T4 : struct, IComponent {
        private readonly bool _track0;
        private readonly bool _track1;
        private readonly bool _track2;
        private readonly bool _track3;
        private readonly bool _track4;

        public bool IsActive {
            [MethodImpl(AggressiveInlining)] get => _track0 || _track1 || _track2 || _track3 || _track4;
        }

        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(byte _) {
            _track0 = World<TWorld>.Components<T0>.Instance.TrackChanged;
            _track1 = World<TWorld>.Components<T1>.Instance.TrackChanged;
            _track2 = World<TWorld>.Components<T2>.Instance.TrackChanged;
            _track3 = World<TWorld>.Components<T3>.Instance.TrackChanged;
            _track4 = World<TWorld>.Components<T4>.Instance.TrackChanged;
        }

        [MethodImpl(AggressiveInlining)]
        public void ApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) World<TWorld>.Components<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track1) World<TWorld>.Components<T1>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track2) World<TWorld>.Components<T2>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track3) World<TWorld>.Components<T3>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track4) World<TWorld>.Components<T4>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(bool _) {
            _track0 = BurstView<TWorld>.ComponentMasks<T0>.Instance.TrackChanged;
            _track1 = BurstView<TWorld>.ComponentMasks<T1>.Instance.TrackChanged;
            _track2 = BurstView<TWorld>.ComponentMasks<T2>.Instance.TrackChanged;
            _track3 = BurstView<TWorld>.ComponentMasks<T3>.Instance.TrackChanged;
            _track4 = BurstView<TWorld>.ComponentMasks<T4>.Instance.TrackChanged;
        }
        
        [MethodImpl(AggressiveInlining)]
        public void BurstApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) BurstView<TWorld>.ComponentMasks<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track1) BurstView<TWorld>.ComponentMasks<T1>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track2) BurstView<TWorld>.ComponentMasks<T2>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track3) BurstView<TWorld>.ComponentMasks<T3>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track4) BurstView<TWorld>.ComponentMasks<T4>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }
        #endif
    }

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    #if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AOT", "IL2091", Justification = "Type metadata is preserved by the registration path.")]
    #endif
    internal readonly struct ChangedTracker<TWorld, T0, T1, T2, T3, T4, T5> : IChangedTracker<TWorld> where TWorld : struct, IWorldType
        where T0 : struct, IComponent
        where T1 : struct, IComponent
        where T2 : struct, IComponent
        where T3 : struct, IComponent
        where T4 : struct, IComponent
        where T5 : struct, IComponent {
        private readonly bool _track0;
        private readonly bool _track1;
        private readonly bool _track2;
        private readonly bool _track3;
        private readonly bool _track4;
        private readonly bool _track5;

        public bool IsActive {
            [MethodImpl(AggressiveInlining)] get => _track0 || _track1 || _track2 || _track3 || _track4 || _track5;
        }

        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(byte _) {
            _track0 = World<TWorld>.Components<T0>.Instance.TrackChanged;
            _track1 = World<TWorld>.Components<T1>.Instance.TrackChanged;
            _track2 = World<TWorld>.Components<T2>.Instance.TrackChanged;
            _track3 = World<TWorld>.Components<T3>.Instance.TrackChanged;
            _track4 = World<TWorld>.Components<T4>.Instance.TrackChanged;
            _track5 = World<TWorld>.Components<T5>.Instance.TrackChanged;
        }

        [MethodImpl(AggressiveInlining)]
        public void ApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) World<TWorld>.Components<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track1) World<TWorld>.Components<T1>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track2) World<TWorld>.Components<T2>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track3) World<TWorld>.Components<T3>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track4) World<TWorld>.Components<T4>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track5) World<TWorld>.Components<T5>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }

        #if FFS_ECS_BURST
        [MethodImpl(AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Local
        public ChangedTracker(bool _) {
            _track0 = BurstView<TWorld>.ComponentMasks<T0>.Instance.TrackChanged;
            _track1 = BurstView<TWorld>.ComponentMasks<T1>.Instance.TrackChanged;
            _track2 = BurstView<TWorld>.ComponentMasks<T2>.Instance.TrackChanged;
            _track3 = BurstView<TWorld>.ComponentMasks<T3>.Instance.TrackChanged;
            _track4 = BurstView<TWorld>.ComponentMasks<T4>.Instance.TrackChanged;
            _track5 = BurstView<TWorld>.ComponentMasks<T5>.Instance.TrackChanged;
        }
        
        [MethodImpl(AggressiveInlining)]
        public void BurstApplyBlock(uint segmentIdx, byte segmentBlockIdx, ulong entitiesMask, byte chunkBlockIdx, uint chunkIdx) {
            if (_track0) BurstView<TWorld>.ComponentMasks<T0>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track1) BurstView<TWorld>.ComponentMasks<T1>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track2) BurstView<TWorld>.ComponentMasks<T2>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track3) BurstView<TWorld>.ComponentMasks<T3>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track4) BurstView<TWorld>.ComponentMasks<T4>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
            if (_track5) BurstView<TWorld>.ComponentMasks<T5>.Instance.SetChangedBitBatch(entitiesMask, segmentIdx, segmentBlockIdx, chunkBlockIdx, chunkIdx);
        }
        #endif
    }
    #endregion

    #endif
}
