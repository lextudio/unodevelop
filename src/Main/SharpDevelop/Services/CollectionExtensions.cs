using System.Collections.Generic;

namespace ICSharpCode.SharpDevelop
{
    internal static class CollectionExtensions
    {
        public static void AddRange<T>(this ICollection<T> list, IEnumerable<T> elements)
        {
            foreach (var element in elements)
                list.Add(element);
        }
    }
}
