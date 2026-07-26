using System;
using ICSharpCode.TypeSystem;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Refactoring;

namespace ICSharpCode.SharpDevelop.Dom
{
	public interface IEntityModelContext
	{
		IProject Project { get; }
		ICompilation GetCompilation();
		bool IsBetterPart(IUnresolvedTypeDefinition part1, IUnresolvedTypeDefinition part2);
		string AssemblyName { get; }
		string FullAssemblyName { get; }
		string Location { get; }
		bool IsValid { get; }
	}

	public class ProjectEntityModelContext : IEntityModelContext
	{
		readonly IProject project;
		readonly string primaryCodeFileExtension;

		public ProjectEntityModelContext(IProject project, string primaryCodeFileExtension)
		{
			if (project == null)
				throw new ArgumentNullException("project");
			this.project = project;
			this.primaryCodeFileExtension = primaryCodeFileExtension;
		}

		public string AssemblyName
		{
			get { return project.AssemblyName; }
		}

		public string FullAssemblyName
		{
			get
			{
				if (project.ProjectContent != null)
				{
					return project.ProjectContent.FullAssemblyName;
				}
				return project.AssemblyName;
			}
		}

		public string Location
		{
			get { return project.OutputAssemblyFullPath; }
		}

		public IProject Project
		{
			get { return project; }
		}

		public ICompilation GetCompilation()
		{
			return null;
		}

		public bool IsBetterPart(IUnresolvedTypeDefinition part1, IUnresolvedTypeDefinition part2)
		{
			return EntityModelContextUtils.IsBetterPart(part1, part2, primaryCodeFileExtension);
		}

		public bool IsValid
		{
			get { return true; }
		}
	}

	public static class EntityModelContextUtils
	{
		public static bool IsBetterPart(IUnresolvedTypeDefinition part1, IUnresolvedTypeDefinition part2, string primaryCodeFileExtension)
		{
			if (part1 == null)
				throw new ArgumentNullException("part1");
			if (part2 == null)
				throw new ArgumentNullException("part2");
			if (part1.Region.IsEmpty)
				return false;
			if (part2.Region.IsEmpty)
				return true;
			bool part1HasExtension = part1.Region.FileName.EndsWith(primaryCodeFileExtension, StringComparison.OrdinalIgnoreCase);
			bool part2HasExtension = part2.Region.FileName.EndsWith(primaryCodeFileExtension, StringComparison.OrdinalIgnoreCase);
			if (part1HasExtension && !part2HasExtension)
				return true;
			if (part2HasExtension && !part1HasExtension)
				return false;
			return part1.Region.BeginLine < part2.Region.BeginLine;
		}
	}
}
