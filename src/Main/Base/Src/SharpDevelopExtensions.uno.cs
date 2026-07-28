using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.TypeSystem;

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

        // Needed by the classic ICSharpCode.UnitTesting backend's TestSolution.cs. Ported from
        // OpenDevelop's own (much larger, WPF-coupled) SharpDevelopExtensions.cs rather than
        // linking that whole file, which declares its own non-partial SharpDevelopExtensions and
        // would collide with this one.
        public static IProject? GetProject(this ICompilation compilation)
        {
            if (compilation is null) throw new ArgumentNullException(nameof(compilation));
            return (compilation.SolutionSnapshot as ISolutionSnapshotWithProjectMapping)?.GetProject(compilation.MainAssembly);
        }

        public static IProject? GetProject(this IAssembly assembly)
        {
            if (assembly is null) throw new ArgumentNullException(nameof(assembly));
            return (assembly.Compilation.SolutionSnapshot as ISolutionSnapshotWithProjectMapping)?.GetProject(assembly);
        }

        // Needed by the classic ICSharpCode.UnitTesting backend's TestProjectBase.cs.
        public static V? GetOrDefault<K, V>(this IReadOnlyDictionary<K, V> dict, K key)
        {
            dict.TryGetValue(key, out var value);
            return value;
        }
    }
}

