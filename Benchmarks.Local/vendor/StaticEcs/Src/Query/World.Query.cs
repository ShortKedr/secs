#if ((DEBUG || FFS_ECS_ENABLE_DEBUG) && !FFS_ECS_DISABLE_DEBUG)
#define FFS_ECS_DEBUG
#endif

using System;
using System.Runtime.CompilerServices;
using static System.Runtime.CompilerServices.MethodImplOptions;
#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace FFS.Libraries.StaticEcs {
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    public abstract partial class World<TWorld> {

        /// <summary>
        /// Creates a query with no filter (<see cref="Nothing"/>), matching all alive entities.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public static WorldQuery<Nothing> Query() => new(default);

        /// <summary>
        /// Creates a query with a single filter.
        /// <para>Usage: <c>W.Query&lt;All&lt;Position, Velocity&gt;&gt;()</c></para>
        /// </summary>
        /// <typeparam name="TFilter0">
        /// A filter type implementing <see cref="IQueryFilter"/>. Common filters:
        /// <see cref="All{T0}"/>, <see cref="None{T0}"/>, <see cref="Any{T0,T1}"/>.
        /// The same <c>All</c>/<c>None</c>/<c>Any</c> filters accept both components and tags.
        /// </typeparam>
        [MethodImpl(AggressiveInlining)]
        public static WorldQuery<TFilter0> Query<TFilter0>(TFilter0 filter0 = default)
            where TFilter0 : struct, IQueryFilter => new(filter0);

        /// <inheritdoc cref="Query{TFilter0}"/>
        [MethodImpl(AggressiveInlining)]
        public static WorldQuery<And<TFilter0, TFilter1>> Query<TFilter0, TFilter1>(TFilter0 filter0 = default, TFilter1 filter1 = default)
            where TFilter0 : struct, IQueryFilter
            where TFilter1 : struct, IQueryFilter =>
            new (new And<TFilter0, TFilter1>(filter0, filter1));

        /// <inheritdoc cref="Query{TFilter0}"/>
        [MethodImpl(AggressiveInlining)]
        public static WorldQuery<And<TFilter0, TFilter1, TFilter2>> Query<TFilter0, TFilter1, TFilter2>(TFilter0 filter0 = default, TFilter1 filter1 = default, TFilter2 filter2 = default)
            where TFilter0 : struct, IQueryFilter
            where TFilter1 : struct, IQueryFilter
            where TFilter2 : struct, IQueryFilter => new (new And<TFilter0, TFilter1, TFilter2>(filter0, filter1, filter2));

        /// <inheritdoc cref="Query{TFilter0}"/>
        [MethodImpl(AggressiveInlining)]
        public static WorldQuery<And<TFilter0, TFilter1, TFilter2, TFilter3>> Query<TFilter0, TFilter1, TFilter2, TFilter3>(TFilter0 filter0 = default, TFilter1 filter1 = default, TFilter2 filter2 = default, TFilter3 filter3 = default)
            where TFilter0 : struct, IQueryFilter
            where TFilter1 : struct, IQueryFilter
            where TFilter2 : struct, IQueryFilter
            where TFilter3 : struct, IQueryFilter => new (new And<TFilter0, TFilter1, TFilter2, TFilter3>(filter0, filter1, filter2, filter3));

        /// <inheritdoc cref="Query{TFilter0}"/>
        [MethodImpl(AggressiveInlining)]
        public static WorldQuery<And<TFilter0, TFilter1, TFilter2, TFilter3, TFilter4>> Query<TFilter0, TFilter1, TFilter2, TFilter3, TFilter4>(
            TFilter0 filter0 = default, TFilter1 filter1 = default, TFilter2 filter2 = default, TFilter3 filter3 = default, TFilter4 filter4 = default
        )
            where TFilter0 : struct, IQueryFilter
            where TFilter1 : struct, IQueryFilter
            where TFilter2 : struct, IQueryFilter
            where TFilter3 : struct, IQueryFilter
            where TFilter4 : struct, IQueryFilter => new (new And<TFilter0, TFilter1, TFilter2, TFilter3, TFilter4>(filter0, filter1, filter2, filter3, filter4));

        /// <inheritdoc cref="Query{TFilter0}"/>
        [MethodImpl(AggressiveInlining)]
        public static WorldQuery<And<TFilter0, TFilter1, TFilter2, TFilter3, TFilter4, TFilter5>> Query<TFilter0, TFilter1, TFilter2, TFilter3, TFilter4, TFilter5>(
            TFilter0 filter0 = default, TFilter1 filter1 = default, TFilter2 filter2 = default, TFilter3 filter3 = default, TFilter4 filter4 = default, TFilter5 filter5 = default
        )
            where TFilter0 : struct, IQueryFilter
            where TFilter1 : struct, IQueryFilter
            where TFilter2 : struct, IQueryFilter
            where TFilter3 : struct, IQueryFilter
            where TFilter4 : struct, IQueryFilter
            where TFilter5 : struct, IQueryFilter => new (new And<TFilter0, TFilter1, TFilter2, TFilter3, TFilter4, TFilter5>(filter0, filter1, filter2, filter3, filter4, filter5));

        #if ENABLE_IL2CPP
        [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
        #endif
        /// <summary>
        /// Stack-allocated query handle providing zero-allocation entity iteration and batch operations.
        /// Created via <c>World&lt;TWorld&gt;.Query&lt;TFilter&gt;()</c>.
        /// <para>
        /// Iteration modes:
        /// <list type="bullet">
        /// <item><see cref="Entities"/> — strict mode (default, faster). Forbids modifying filtered types on, and destroying/disabling/enabling, entities that are part of the iteration snapshot (i.e. entities that pass the filter and would be visited later). Entities created during iteration and entities that did not pass the filter are not part of the snapshot and remain freely mutable.</item>
        /// <item><see cref="EntitiesFlexible"/> — flexible mode. Same restrictions on filtered-type modifications as strict, but additionally allows destroying / disabling / enabling other snapshot entities during iteration (such entities are excluded from the remaining iteration via cached bitmask updates).</item>
        /// </list>
        /// </para>
        /// <para>
        /// Also supports delegate-based iteration via <c>For()</c>, parallel iteration via <c>ForParallel()</c>,
        /// and batch operations via <c>BatchAdd()</c>, <c>BatchDelete()</c>, etc. (defined in partial Query files).
        /// </para>
        /// </summary>
        /// <typeparam name="TFilter">The filter type constraining which entities match. See <see cref="IQueryFilter"/>.</typeparam>
        public readonly ref partial struct WorldQuery<TFilter> where TFilter : struct, IQueryFilter {
            internal readonly TFilter Filter;

            [MethodImpl(AggressiveInlining)]
            public WorldQuery(TFilter filter) {
                Filter = filter;
            }

            /// <summary>
            /// Returns a strict-mode iterator over matching entities. This is the default and fastest iteration mode.
            /// <para>
            /// In strict mode, the following operations are forbidden on entities that belong to the iteration
            /// snapshot but are not the current one (enforced by assertions in debug builds): modifying filtered
            /// component/tag types (Add/Delete/Enable/Disable), destroying entities, and changing entity status
            /// (Disable/Enable). The "iteration snapshot" is the bitmask of entities that match the filter at the
            /// moment iteration starts. Entities that are <b>not</b> in the snapshot — namely entities created
            /// during iteration and entities that did not pass the filter — are not blocked and may be freely
            /// mutated, created, or destroyed.
            /// </para>
            /// </summary>
            /// <param name="entities">Which entity lifecycle states to include (default: only enabled).</param>
            /// <param name="clusters">Optional cluster IDs to restrict iteration scope. Empty = all clusters.</param>
            /// <returns>A <see cref="QueryStrictIterator{TWorld, TFilter}"/> for foreach enumeration.</returns>
            [MethodImpl(AggressiveInlining)]
            public QueryStrictIterator<TWorld, TFilter> Entities(EntityStatusType entities = EntityStatusType.Enabled,
                                                                 ReadOnlySpan<ushort> clusters = default) {
                return new QueryStrictIterator<TWorld, TFilter>(clusters, Filter, entities);
            }
            
            /// <summary>
            /// Returns a flexible-mode iterator over matching entities. Slower than <see cref="Entities"/>,
            /// but additionally allows destroying / disabling / enabling other snapshot entities during iteration —
            /// such entities are excluded from the remaining iteration via cached bitmask updates.
            /// <para>
            /// Modifying filtered component/tag types on other snapshot entities (Add/Delete/Enable/Disable of the
            /// components the query filters on) remains forbidden in flexible mode as well and is enforced by
            /// assertions in debug builds, same as in strict mode. As in strict mode, entities outside the iteration
            /// snapshot (newly created or non-matching) are not blocked. Flexible differs from strict only in
            /// tolerating entity-level destroy and status-change operations on other snapshot entities.
            /// </para>
            /// </summary>
            /// <param name="entities">Which entity lifecycle states to include (default: only enabled).</param>
            /// <param name="clusters">Optional cluster IDs to restrict iteration scope. Empty = all clusters.</param>
            /// <returns>A <see cref="QueryFlexibleIterator{TWorld, TFilter}"/> for foreach enumeration.</returns>
            [MethodImpl(AggressiveInlining)]
            public QueryFlexibleIterator<TWorld, TFilter> EntitiesFlexible(EntityStatusType entities = EntityStatusType.Enabled,
                                                                           ReadOnlySpan<ushort> clusters = default) {
                return new QueryFlexibleIterator<TWorld, TFilter>(clusters, Filter, entities);
            }
            
            [MethodImpl(AggressiveInlining)]
            internal QueryStrictIterator<TWorld, TFilter> Entities(ReadOnlySpan<uint> chunks, EntityStatusType entities = EntityStatusType.Enabled) {
                return new QueryStrictIterator<TWorld, TFilter>(chunks, Filter, entities);
            }

            [MethodImpl(AggressiveInlining)]
            internal QueryFlexibleIterator<TWorld, TFilter> EntitiesFlexible(ReadOnlySpan<uint> chunks, EntityStatusType entities = EntityStatusType.Enabled) {
                return new QueryFlexibleIterator<TWorld, TFilter>(chunks, Filter, entities);
            }

            /// <summary>
            /// Checks if at least one entity matches the query and returns it.
            /// Optimized single-entity lookup.
            /// </summary>
            /// <param name="entity">The first matching entity, or <c>default</c> if none.</param>
            /// <param name="entities">Which entity lifecycle states to include (default: only enabled).</param>
            /// <param name="clusters">Optional cluster IDs to restrict search scope. Empty = all clusters.</param>
            /// <returns><c>true</c> if a matching entity was found.</returns>
            [MethodImpl(AggressiveInlining)]
            public bool Any(out Entity entity, EntityStatusType entities = EntityStatusType.Enabled,
                            ReadOnlySpan<ushort> clusters = default) {
                return FindFirst(Filter, clusters, entities, out entity, false);
            }

            /// <summary>
            /// Checks if exactly zero or one entity matches the query and returns it.
            /// In debug builds, throws <see cref="StaticEcsException"/> if more than one entity matches.
            /// In release builds, behaves identically to <see cref="Any"/>.
            /// Optimized single-entity lookup.
            /// </summary>
            /// <param name="entity">The single matching entity, or <c>default</c> if none.</param>
            /// <param name="entities">Which entity lifecycle states to include (default: only enabled).</param>
            /// <param name="clusters">Optional cluster IDs to restrict search scope. Empty = all clusters.</param>
            /// <returns><c>true</c> if exactly one entity was found; <c>false</c> if zero.</returns>
            [MethodImpl(AggressiveInlining)]
            public bool One(out Entity entity, EntityStatusType entities = EntityStatusType.Enabled,
                            ReadOnlySpan<ushort> clusters = default) {
                return FindFirst(Filter, clusters, entities, out entity, true);
            }

            /// <summary>
            /// Checks whether the given <paramref name="entity"/> belongs to the result set of this query —
            /// i.e. it passes the query's filter and lies within the configured scope.
            /// </summary>
            /// <param name="entity">Entity to test.</param>
            /// <param name="entities">Which entity lifecycle states are accepted (default: only enabled).</param>
            /// <param name="clusters">Optional cluster IDs restricting scope. Empty = all clusters.</param>
            /// <returns><c>true</c> if the entity is contained in the query result.</returns>
            [MethodImpl(AggressiveInlining)]
            public bool Contains(Entity entity, EntityStatusType entities = EntityStatusType.Enabled,
                                 ReadOnlySpan<ushort> clusters = default) {
                switch (entities) {
                    case EntityStatusType.Enabled when !entity.IsEnabled: return false;
                    case EntityStatusType.Disabled when !entity.IsDisabled: return false;
                }

                var found = true;
                if (!clusters.IsEmpty) {
                    var clusterId = entity.ClusterId;
                    for (var i = 0; i < clusters.Length; i++) {
                        if (clusters[i] == clusterId) {
                            break;
                        }
                    }

                    found = false;
                }

                return found && entity.IsMatch(Filter);
            }
        }
        
        /// <summary>
        /// Provides indexed access to a contiguous block of entities during low-level query iteration.
        /// Used internally by <c>ForBlock</c>-style iteration for maximum performance via unsafe pointers.
        /// </summary>
        #if ENABLE_IL2CPP
        [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
        #endif
        public ref struct EntityBlock {
            internal Entity Value;
            internal uint Offset;

            [MethodImpl(AggressiveInlining)]
            internal EntityBlock(Entity value, uint offset) {
                Value = value;
                Offset = offset;
            }

            /// <summary>Gets the entity at the specified index within this block.</summary>
            public Entity this[uint idx] {
                [MethodImpl(AggressiveInlining)]
                get {
                    Value.IdWithOffset = Offset + idx;
                    return Value;
                }
            }
        }
    }

    /// <summary>
    /// Interface for query filter types that determine which entities match a query.
    /// Filters operate at two levels: chunk-level (coarse, via heuristic bitmasks) and entity-level (fine, via presence bitmasks).
    /// <para>
    /// Built-in filter implementations for components and tags:
    /// <see cref="All{T0}"/>, <see cref="None{T0}"/>, <see cref="Any{T0, T1}"/> — accept both components and tags as type arguments,
    /// <see cref="AllOnlyDisabled{T0}"/>, <see cref="AllWithDisabled{T0}"/>,
    /// <see cref="NoneWithDisabled{T0}"/>, <see cref="AnyOnlyDisabled{T0, T1}"/>, <see cref="AnyWithDisabled{T0, T1}"/> — components only.
    /// Combine multiple filters as query type parameters for complex matching logic.
    /// </para>
    /// </summary>
    public interface IQueryFilter {
        /// <summary>
        /// Coarse-grained chunk-level filtering. Returns heuristic bitmask
        /// to quickly skip entire blocks (64 entities) that cannot match.
        /// The caller ANDs the result with the current chunk mask.
        /// </summary>
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType;

        /// <summary>
        /// Fine-grained entity-level filtering. Returns actual presence bitmask
        /// to determine exactly which entities in this block match.
        /// The caller ANDs the result with the current entities mask.
        /// </summary>
        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType;

        #if FFS_ECS_DEBUG
        /// <summary>
        /// Debug-only: increments/decrements blocker counters on filtered component/tag types
        /// to detect forbidden structural changes during strict-mode iteration. The check is
        /// scoped to entities present in the current iteration snapshot; entities outside the
        /// snapshot (created mid-iteration or non-matching) are not blocked.
        /// </summary>
        public void Block<TWorld>(int val) where TWorld : struct, IWorldType;
        #endif

        #if FFS_ECS_BURST
        #if FFS_ECS_DEBUG
        public void BurstBlock<TWorld>(int val) where TWorld : struct, IWorldType {} // TODO
        #endif
        
        public ulong BurstFilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType;

        public ulong BurstFilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType;
        #endif
    }

    /// <summary>
    /// Controls which entities are iterated based on their lifecycle state (entity-level, not component-level).
    /// </summary>
    public enum EntityStatusType : byte {
        /// <summary>Only iterate entities that are enabled (default).</summary>
        Enabled = 0,
        /// <summary>Only iterate entities that are disabled.</summary>
        Disabled = 1,
        /// <summary>Iterate all entities regardless of enabled/disabled state.</summary>
        Any = 2,
    }

    /// <summary>
    /// Controls which component presence states are considered when filtering.
    /// Used by some query overloads to parameterize filter behavior at runtime.
    /// </summary>
    public enum ComponentStatus : byte {
        /// <summary>Only match enabled components.</summary>
        Enabled = 0,
        /// <summary>Match components in any state (enabled or disabled).</summary>
        Any = 1,
        /// <summary>Only match disabled components.</summary>
        Disabled = 2,
    }

    /// <summary>
    /// Controls the iteration mode for queries, affecting performance and safety guarantees.
    /// The default value (<see cref="Strict"/>) provides the fastest iteration.
    /// </summary>
    public enum QueryMode {
        /// <summary>
        /// Strict mode (default): forbids, on entities that are part of the iteration snapshot but are not the
        /// current one, both modifications of filtered component/tag types (Add/Delete/Enable/Disable) and
        /// entity-level operations (Destroy/Disable/Enable). The "iteration snapshot" is the bitmask of entities
        /// matching the filter at the moment iteration starts. Entities created during iteration and entities that
        /// did not pass the filter are <b>not</b> in the snapshot and are not blocked — they may be freely created,
        /// configured, mutated, or destroyed inside the loop.
        /// Faster — uses fast-path for full blocks, skips re-checking bitmasks. Use when you do not need to
        /// destroy / disable / enable other snapshot entities during the loop.
        /// </summary>
        Strict,
        /// <summary>
        /// Flexible mode: allows destroying / disabling / enabling other snapshot entities during iteration — such
        /// entities are correctly excluded from the remaining iteration via cached bitmask updates.
        /// <para>
        /// Modifying filtered component/tag types on other snapshot entities (Add/Delete/Enable/Disable of the
        /// components the query filters on) is still forbidden and asserted in debug builds, same as in Strict.
        /// As in Strict mode, entities outside the iteration snapshot (newly created or non-matching) are not
        /// blocked. Flexible mode differs from Strict only in tolerating entity-level destroy / status-change
        /// operations on other snapshot entities.
        /// </para>
        /// </summary>
        Flexible
    }

    /// <summary>
    /// Cached bitmask for a single block (64 entities) during flexible-mode query iteration.
    /// Allows entity-level destroy/disable/enable operations on other entities to update the mask
    /// mid-iteration without invalidating the iterator.
    /// </summary>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    public struct BlockMaskCache {
        /// <summary>Bitmask of matching entities in this block. Bits are cleared when another entity is destroyed / disabled / enabled during flexible-mode iteration so that entity is skipped.</summary>
        public ulong EntitiesMask;
        /// <summary>Index of the next non-empty global block in the iteration chain, or -1 if this is the last block.</summary>
        public int NextGlobalBlock;
    }
    

    /// <summary>
    /// Holds the cached block masks for a single active flexible-mode query. Registered on the world's
    /// internal entity-lifecycle update lists (<c>PushQueryDataForDestroy</c> / <c>PushQueryDataForDisable</c> /
    /// <c>PushQueryDataForEnable</c>) when flexible iteration begins and unregistered when it ends.
    /// Entity-level destroy and status-change operations invoke <see cref="Update"/> to clear the affected
    /// bits so the iterator skips entities that are no longer eligible.
    /// </summary>
    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, Const.IL2CPPNullChecks)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, Const.IL2CPPArrayBoundsChecks)]
    #endif
    public struct QueryData {
        /// <summary>Array of cached block masks for the active query. Indexed by global block index.</summary>
        public BlockMaskCache[] Blocks;
        
        [MethodImpl(AggressiveInlining)]
        public void Update(ulong invertedBlockEntityMask, uint segmentIdx, byte segmentBlockIdx) {
            Blocks[(segmentIdx << Const.BLOCKS_IN_SEGMENT_SHIFT) + segmentBlockIdx].EntitiesMask &= invertedBlockEntityMask;
        }
    }
}