﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Squared.Util {
    public interface IRefComparer<T> {
        int Compare (ref T lhs, ref T rhs);
    }

    public struct RefComparerAdapter<TComparer, TElement> : IRefComparer<TElement>
        where TComparer : IComparer<TElement> 
    {
        public TComparer Comparer;

        public RefComparerAdapter (TComparer comparer) {
            Comparer = comparer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (ref TElement lhs, ref TElement rhs) {
            return Comparer.Compare(lhs, rhs);
        }
    }

    public class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class {

        public bool Equals (T x, T y) {
            return (x == y);
        }

        public int GetHashCode (T obj) {
            return obj.GetHashCode();
        }
    }

    public static class Sort {
        public static void FastCLRSort<TElement, TComparer>(
            TElement[] data, TComparer comparer, int? offset = null, int? count = null
        )
            where TComparer : IComparer<TElement>
        {
            var adapter = new RefComparerAdapter<TComparer, TElement>(comparer);
            FastCLRSortRef(data, adapter, offset, count);
        }

        public static void IndexedSort<TElement, TComparer>(
            TElement[] data, int[] indices,
            TComparer comparer, int? offset = null, int? count = null
        )
            where TComparer : IComparer<TElement>
        {
            var adapter = new RefComparerAdapter<TComparer, TElement>(comparer);
            IndexedSortRef(data, indices, adapter, offset, count);
        }

        public static void FastCLRSortRef<TElement, TComparer>(
            TElement[] data, TComparer comparer, int? offset = null, int? count = null
        )
            where TComparer : IRefComparer<TElement>
        {
            int actualOffset = offset.GetValueOrDefault(0),
                actualCount = count.GetValueOrDefault(data.Length - actualOffset);

            var sorter = new FastCLRSorter<TElement, TComparer>(data, comparer);

            sorter.Sort(actualOffset, actualCount);
        }

        public static void IndexedSortRef<TElement, TComparer>(
            TElement[] data, int[] indices,
            TComparer comparer, int? offset = null, int? count = null
        )
            where TComparer : IRefComparer<TElement>
        {
            int actualOffset = offset.GetValueOrDefault(0),
                actualCount = count.GetValueOrDefault(data.Length - actualOffset);

            var sorter = new IndexedSorter<TElement, TComparer>(data, indices, comparer);

            sorter.Sort(
                actualOffset,
                actualCount
            );
        }
    }
}
