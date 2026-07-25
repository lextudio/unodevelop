using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ICSharpCode.TypeSystem
{
	public sealed class MinimalCorlib : IAssemblyReference
	{
		public static readonly MinimalCorlib Instance = new MinimalCorlib();

		public IAssembly Resolve(ITypeResolveContext context) => null;

		public ICompilation CreateCompilation()
		{
			return new SimpleCompilation(null, new IAssemblyReference[] { this });
		}
	}

	public static class FreezableHelper
	{
		public static void Freeze(object obj)
		{
		}
	}

	public static class LazyInit
	{
		public static T GetOrSet<T>(ref T target, T value) where T : class
		{
			T oldValue = System.Threading.Interlocked.CompareExchange(ref target, value, null);
			return oldValue ?? value;
		}
	}

	public sealed class CallbackOnDispose : IDisposable
	{
		Action callback;

		public CallbackOnDispose(Action callback)
		{
			this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
		}

		public void Dispose()
		{
			Interlocked_Exchange(ref callback, null)?.Invoke();
		}

		static Action Interlocked_Exchange(ref Action location, Action value)
		{
			return System.Threading.Interlocked.Exchange(ref location, value);
		}
	}

	public class SimpleCompilation : ICompilation
	{
		public IAssembly MainAssembly { get; }
		public IList<IAssembly> Assemblies { get; } = new List<IAssembly>();
		public IList<IAssembly> ReferencedAssemblies { get; } = new List<IAssembly>();
		public ITypeResolveContext TypeResolveContext { get; }
		public INamespace RootNamespace => null;
		public StringComparer NameComparer => StringComparer.Ordinal;
		public ISolutionSnapshot SolutionSnapshot => null;
		public CacheManager CacheManager { get; } = new CacheManager();

		public SimpleCompilation(IUnresolvedAssembly mainAssembly, IEnumerable<IAssemblyReference> assemblyReferences)
		{
			this.TypeResolveContext = new SimpleTypeResolveContext(this);
			try {
				this.MainAssembly = mainAssembly?.Resolve(this.TypeResolveContext);
			} catch (NotImplementedException) {
				this.MainAssembly = null;
			}
			if (this.MainAssembly != null)
				Assemblies.Add(this.MainAssembly);
		}

		public INamespace GetNamespaceForExternAlias(string alias) => RootNamespace;

		public IType FindType(KnownTypeCode typeCode) => null;
	}

	public class SimpleTypeResolveContext : ITypeResolveContext
	{
		public ICompilation Compilation { get; }
		public IAssembly CurrentAssembly { get; }
		public ITypeDefinition CurrentTypeDefinition { get; }
		public IMember CurrentMember { get; }

		public SimpleTypeResolveContext(ICompilation compilation)
		{
			this.Compilation = compilation;
		}

		public SimpleTypeResolveContext(ITypeDefinition typeDefinition)
		{
			this.Compilation = typeDefinition?.Compilation;
			this.CurrentTypeDefinition = typeDefinition;
		}

		SimpleTypeResolveContext(ICompilation compilation, IAssembly assembly, ITypeDefinition typeDefinition, IMember member)
		{
			this.Compilation = compilation;
			this.CurrentAssembly = assembly;
			this.CurrentTypeDefinition = typeDefinition;
			this.CurrentMember = member;
		}

		public ITypeResolveContext WithCurrentTypeDefinition(ITypeDefinition typeDefinition)
			=> new SimpleTypeResolveContext(Compilation, CurrentAssembly, typeDefinition, CurrentMember);

		public ITypeResolveContext WithCurrentMember(IMember member)
			=> new SimpleTypeResolveContext(Compilation, CurrentAssembly, CurrentTypeDefinition, member);
	}

	public class DefaultUnresolvedMethod
	{
		public bool IsExtensionMethod { get; set; }
	}

	public static class ReflectionHelperExtensions
	{
		public static ITypeReference ParseReflectionName(this string reflectionTypeName)
		{
			return null;
		}
	}

	public class FastSerializer
	{
		public void Serialize(BinaryWriter writer, object instance)
		{
		}

		public object Deserialize(BinaryReader reader)
		{
			return null;
		}
	}

	public class BinaryWriterWith7BitEncodedInts : BinaryWriter
	{
		public BinaryWriterWith7BitEncodedInts(Stream stream) : base(stream) { }

		public new void Write7BitEncodedInt(int value) => base.Write7BitEncodedInt(value);
	}

	public class BinaryReaderWith7BitEncodedInts : BinaryReader
	{
		public BinaryReaderWith7BitEncodedInts(Stream stream) : base(stream) { }

		public new int Read7BitEncodedInt() => base.Read7BitEncodedInt();
	}

	public static class ProjectContentExtensions
	{
		public static IEnumerable<IUnresolvedTypeDefinition> GetAllTypeDefinitions(this IProjectContent projectContent)
		{
			if (projectContent == null)
				yield break;
			foreach (var file in projectContent.Files) {
				foreach (var td in AllNested(file.TopLevelTypeDefinitions)) {
					yield return td;
				}
			}
		}

		public static IEnumerable<ITypeDefinition> GetAllTypeDefinitions(this IAssembly assembly)
		{
			yield break;
		}

		static IEnumerable<IUnresolvedTypeDefinition> AllNested(IEnumerable<IUnresolvedTypeDefinition> types)
		{
			foreach (var t in types) {
				yield return t;
				foreach (var n in AllNested(t.NestedTypes))
					yield return n;
			}
		}
	}
}
