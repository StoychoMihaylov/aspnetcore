// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.AspNetCore.Components.Rendering
{
    /// <summary>
    /// A specialized alternative dictionary tuned entirely towards speeding up RenderTreeBuilder's
    /// ProcessDuplicateAttributes method. 
    /// </summary>
    /// <remarks>
    /// It's faster than a normal Dictionary[string, int] because instead of hashing the entire key
    /// (considering every character), it performs a much cheaper hash that only considers a few
    /// characters. This detects non-matches much faster, which is ideal for attribute splatting,
    /// because the vast majority of attributes aren't overridden.
    /// 
    /// When tweaking the ComplexTable benchmark to pass 3 extra splatted parameters per cell, this
    /// optimization improves the timings by 17%. It's also about 4% faster than using
    /// Dictionary[string, int] with custom comparer to achieve the simplified hash, as the API is
    /// a more precise match to the "add-or-return-existing" semantics needed.
    ///
    /// This dictionary shouldn't be used in other situations because it may perform much worse than
    /// a Dictionary[string, int] if most of the lookups/insertions match existing entries.
    /// </remarks>
    internal class MultipleAttributesDictionary
    {
        public const int InitialCapacity = 79;

        private string[] _keys = new string[InitialCapacity];
        private int[] _values = new int[InitialCapacity];
        private int _capacity = InitialCapacity;

        public void Clear()
        {
            Array.Clear(_keys, 0, _keys.Length);
            Array.Clear(_values, 0, _values.Length);
        }

        public bool TryAdd(string key, int value, out int existingValue)
        {
            if (TryFindIndex(key, out var index))
            {
                existingValue = _values[index];
                return false;
            }
            else
            {
                if (index < 0) // Indicates that storage is full
                {
                    ExpandStorage();
                    TryFindIndex(key, out index);
                    Debug.Assert(index >= 0);
                }

                _keys[index] = key;
                _values[index] = value;
                existingValue = default;
                return true;
            }
        }

        public void Replace(string key, int value)
        {
            if (TryFindIndex(key, out var index))
            {
                _values[index] = value;
            }
            else
            {
                throw new InvalidOperationException($"Key not found: '{key}'");
            }
        }

        private bool TryFindIndex(string key, out int existingIndexOrInsertionPosition)
        {
            var hashCode = GetSimpleHashCode(key);
            var startIndex = hashCode % _capacity;
            if (startIndex < 0)
            {
                startIndex += _capacity;
            }
            var candidateIndex = startIndex;

            do
            {
                var candidateKey = _keys[candidateIndex];
                if (candidateKey == null)
                {
                    existingIndexOrInsertionPosition = candidateIndex;
                    return false;
                }

                if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    existingIndexOrInsertionPosition = candidateIndex;
                    return true;
                }

                if (++candidateIndex >= _capacity)
                {
                    candidateIndex = 0;
                }
            }
            while (candidateIndex != startIndex);

            // We didn't find the key, and there's no empty slot in which we could insert it.
            // Storage is full.
            existingIndexOrInsertionPosition = -1;
            return false;
        }

        private void ExpandStorage()
        {
            var oldKeys = _keys;
            var oldValues = _values;
            _capacity = _capacity * 2;
            _keys = new string[_capacity];
            _values = new int[_capacity];

            for (var i = 0; i < oldKeys.Length; i++)
            {
                var key = oldKeys[i];
                if (!(key is null))
                {
                    var value = oldValues[i];
                    var didInsert = TryAdd(key, value, out _);
                    Debug.Assert(didInsert);
                }
            }
        }

        private static int GetSimpleHashCode(string key)
        {
            var keyLength = key.Length;
            if (keyLength > 0)
            {
                // Consider just the first, middle, and last characters
                // This will produce a distinct result for a sufficiently large
                // proportion of attribute names
                return unchecked(17
                    + 31 * char.ToLowerInvariant(key[0])
                    + 961 * char.ToLowerInvariant(key[keyLength / 2])
                    + 29791 * char.ToLowerInvariant(key[keyLength - 1]));
            }
            else
            {
                return default;
            }
        }
    }
}
