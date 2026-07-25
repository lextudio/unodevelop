using System;
using System.Collections.Generic;

namespace ICSharpCode.TypeSystem
{
	public class MemberResolveResult : ResolveResult
	{
		public IMember Member { get; }

		public MemberResolveResult(IType targetType, IMember member)
			: base(member != null ? member.ReturnType : targetType)
		{
			this.Member = member;
		}
	}

	public class TypeResolveResult : ResolveResult
	{
		public ITypeDefinition ResolvedClass { get; }

		public TypeResolveResult(ITypeDefinition resolvedClass)
			: base(resolvedClass)
		{
			this.ResolvedClass = resolvedClass;
		}
	}

	public class NamespaceResolveResult : ResolveResult
	{
		public INamespace Namespace { get; }

		public NamespaceResolveResult(INamespace ns)
			: base(null)
		{
			this.Namespace = ns;
		}
	}

	public class LocalResolveResult : ResolveResult
	{
		public IVariable Variable { get; }
		public string VariableName => Variable != null ? Variable.Name : null;
		public DomRegion VariableDefinitionRegion => Variable != null ? Variable.Region : DomRegion.Empty;
		public IField Field { get; }

		public LocalResolveResult(IVariable variable)
			: base(variable != null ? variable.Type : null)
		{
			this.Variable = variable;
		}
	}

	public class ErrorResolveResult : ResolveResult
	{
		public static readonly ErrorResolveResult UnknownError = new ErrorResolveResult();

		public ErrorResolveResult() : base(null)
		{
		}
	}

	public static class ResolveResultExtensions
	{
		public static DomRegion GetDefinitionRegion(this ResolveResult result)
		{
			if (result is MemberResolveResult mrr && mrr.Member != null)
				return mrr.Member.Region;
			if (result is TypeResolveResult trr && trr.ResolvedClass != null)
				return trr.ResolvedClass.Region;
			if (result is LocalResolveResult lrr)
				return lrr.VariableDefinitionRegion;
			return DomRegion.Empty;
		}

		public static bool IsError(this ResolveResult result)
		{
			return result == null || result is ErrorResolveResult;
		}
	}
}
