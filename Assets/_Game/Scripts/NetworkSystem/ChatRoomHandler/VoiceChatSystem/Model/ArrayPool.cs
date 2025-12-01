using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NyxMachina.Multiplayer
{
    public class ArrayPool<T>
    {
        private readonly Stack<T[]> _pool = new Stack<T[]>();
        private readonly int _arraySize;

        public ArrayPool(int capacity, int arraySize)
        {
            _arraySize = arraySize;
            for (int i = 0; i < capacity; i++)
            {
                _pool.Push(new T[arraySize]);
            }
        }

        public T[] Rent()
        {
            lock (_pool)
            {
                if (_pool.Count > 0) return _pool.Pop();
            }
            // Fallback if pool empty (shouldn't happen if sized correctly)
            return new T[_arraySize];
        }

        public void Return(T[] item)
        {
            lock (_pool)
            {
                _pool.Push(item);
            }
        }
    }
}
