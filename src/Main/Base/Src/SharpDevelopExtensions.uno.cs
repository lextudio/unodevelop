using System;

namespace ICSharpCode.SharpDevelop
{
    public static partial class SharpDevelopExtensions
    {
        public static T GetService<T>(this IServiceProvider provider) where T : class
        {
            return provider.GetService(typeof(T)) as T;
        }
    }
}

