using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ICSharpCode.SharpDevelop
{
    public static partial class SharpDevelopExtensions
    {
        public static T GetService<T>(this IServiceProvider provider) where T : class
        {
            return provider.GetService(typeof(T)) as T;
        }

        public static ReadOnlyCollection<T> AsReadOnly<T>(this IList<T> arr)
        {
            return new ReadOnlyCollection<T>(arr);
        }
    }
}

