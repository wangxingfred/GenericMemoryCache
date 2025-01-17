﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System.Runtime.Caching.Generic
{
    public class MemorySubCache<TKey, TValue> : AbstractEvictingCache<TKey, TValue>
    {
        private int capacity;

        public MemorySubCache(IManagedCache<TKey, TValue> parent, int capacity)
            : base(parent, capacity)
        {
            if (parent == null)
            {
                throw new ArgumentNullException("parent", "cannot be null");
            }
            this.capacity = capacity;
        }

        protected ICacheEviction<TKey, TValue> State { get; private set; }

#if THREAD_SAFE
        protected readonly object SyncRoot = new object();
#endif

        protected bool IsEvicting;

        protected override void Prepare(ICachePolicy<TKey, TValue> policy)
        {
            var type =
                policy != null ?
                policy.GetCacheEvictionType() ?? typeof(NoCacheEviction<TKey, TValue>)
                :
                typeof(NoCacheEviction<TKey, TValue>);
            // Necessary test, as the policy (which the eviction is derived from)
            // may never have been set yet
            if (State != null)
            {
                State.Clear();
            }
            State = (ICacheEviction<TKey, TValue>)Activator.CreateInstance(type, this, Capacity);
        }

        protected override IDictionary<TKey, TValue> GetSnapshot()
        {
#if THREAD_SAFE
            lock (SyncRoot)
            {
#endif
                return new Dictionary<TKey, TValue>(State);
#if THREAD_SAFE
            }
#endif
        }

        protected bool InternalTryGet(TKey key, out TValue value)
        {
            value = default(TValue);
            return State.Handle(CacheAccess.Get, key, ref value, true);
        }

        protected bool InternalSet(TKey key, TValue value, bool isPut)
        {            
            if (capacity <= State.Count && !State.ContainsKey(key))
            {
                // Cache is "full", and it's an Add; we have to evict one or more key(s)
                try
                {
                    if (!IsEvicting)
                    {
                        // We can evict iff we are not already in the process of doing so
                        // (thus, forbid reentrant evictions to keep the semantic simple)
                        IsEvicting = true;
                        if (!State.Evict())
                        {
                            // Something went wrong, the eviction couldn't do its job
                            throw new InvalidOperationException("could not evict");
                        }
                    }
                    else
                    {
                        // Otherwise, it means the main cache's policy itself is somehow
                        // indirectly trying to add always many more items, in excess of
                        // the cache's capacity
                        throw new NotSupportedException("reentrant evictions are not supported");
                    }
                }
                finally
                {
                    IsEvicting = false;
                }
            }
            return State.Handle(CacheAccess.Set, key, ref value, isPut);
        }

        protected bool InternalRemove(TKey key)
        {
            return State.Evict(EvictionReason.Removal, key);
        }

#region IManagedCache<TKey, TValue> implementation
        public override bool Contains(TKey key)
        {
#if THREAD_SAFE
            lock (SyncRoot)
            {
#endif
                return State.ContainsKey(key);
#if THREAD_SAFE
            }
#endif
        }

        public override bool TryGet(TKey key, out TValue value)
        {
#if THREAD_SAFE
            lock (SyncRoot)
            {
#endif
                return InternalTryGet(key, out value);
#if THREAD_SAFE
            }
#endif
        }

        public override TValue Get(TKey key)
        {
#if THREAD_SAFE
            lock (SyncRoot)
            {
#endif
                TValue cached;
                if (!InternalTryGet(key, out cached))
                {
                    throw new KeyNotFoundException();
                }
                return cached;
#if THREAD_SAFE
            }
#endif
        }

        public override TValue GetOrAdd(TKey key, TValue value)
        {
#if THREAD_SAFE
            lock (SyncRoot)
            {
#endif
                TValue cached;
                if (!InternalTryGet(key, out cached))
                {
                    InternalSet(key, value, false);
                    return value;
                }
                return cached;
#if THREAD_SAFE
            }
#endif
        }

        public override TValue GetOrAdd<TContext>(TKey key, Func<TContext, TValue> updater, TContext context)
        {
#if THREAD_SAFE
            lock (SyncRoot)
            {
#endif
                TValue cached;
                if (!InternalTryGet(key, out cached))
                {
                    cached = updater(context);
                    InternalSet(key, cached, false);
                }
                return cached;
#if THREAD_SAFE
            }
#endif
        }

        public override bool Add(TKey key, TValue value)
        {
#if THREAD_SAFE
            lock (SyncRoot)
            {
#endif
            return InternalSet(key, value, false);
#if THREAD_SAFE
            }
#endif
        }

        public override void Put(TKey key, TValue value)
        {
#if THREAD_SAFE
            lock (SyncRoot)
            {
#endif
            InternalSet(key, value, true);
#if THREAD_SAFE
            }
#endif
        }

        public override bool Remove(TKey key)
        {
#if THREAD_SAFE
            lock (SyncRoot)
            {
#endif
            return InternalRemove(key);
#if THREAD_SAFE
            }
#endif
        }

        public override int Capacity
        {
            get
            {
                return capacity;
            }
        }

        public override int Count
        {
            get
            {
#if THREAD_SAFE
                lock (SyncRoot)
                {
#endif
                return State != null ? State.Count : 0;
#if THREAD_SAFE
                }
#endif
            }
        }
#endregion
    }
}