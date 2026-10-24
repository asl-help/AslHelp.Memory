using System.Collections.Generic;

namespace AslHelp.Memory.Watching;

public partial class ArrayWatcher<T>
{
    // Compares arrays element by element, because every read creates a new array and the default comparer would
    // report a change on every tick.
    private sealed class ContentsComparer : IEqualityComparer<T[]?>
    {
        public static readonly ContentsComparer Instance = new();

        public bool Equals(T[]? x, T[]? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null || x.Length != y.Length)
            {
                return false;
            }

            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < x.Length; i++)
            {
                if (!comparer.Equals(x[i], y[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(T[]? obj)
        {
            if (obj is null)
            {
                return 0;
            }

            var comparer = EqualityComparer<T>.Default;
            int hash = 17;
            foreach (var element in obj)
            {
                hash = unchecked((hash * 31) + comparer.GetHashCode(element));
            }

            return hash;
        }
    }
}
