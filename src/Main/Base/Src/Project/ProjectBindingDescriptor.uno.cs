using System;
using System.Threading;

namespace ICSharpCode.SharpDevelop.Project;

/// <summary>
/// Upstream-aligned project binding descriptor used by IProjectService.
/// This Uno migration variant intentionally skips AddInTree/Codon wiring for now.
/// </summary>
public sealed class ProjectBindingDescriptor
{
	private readonly Func<IProjectBinding?>? _bindingFactory;
	private IProjectBinding? _binding;

	public ProjectBindingDescriptor(IProjectBinding binding, string language, string projectFileExtension, Guid typeGuid, string[] codeFileExtensions)
	{
		ArgumentNullException.ThrowIfNull(binding);
		ArgumentNullException.ThrowIfNull(language);
		ArgumentNullException.ThrowIfNull(projectFileExtension);
		ArgumentNullException.ThrowIfNull(codeFileExtensions);

		_binding = binding;
		Language = language;
		ProjectFileExtension = projectFileExtension;
		TypeGuid = typeGuid;
		CodeFileExtensions = codeFileExtensions;
	}

	public ProjectBindingDescriptor(Func<IProjectBinding?> bindingFactory, string language, string projectFileExtension, Guid typeGuid, string[] codeFileExtensions)
	{
		ArgumentNullException.ThrowIfNull(bindingFactory);
		ArgumentNullException.ThrowIfNull(language);
		ArgumentNullException.ThrowIfNull(projectFileExtension);
		ArgumentNullException.ThrowIfNull(codeFileExtensions);

		_bindingFactory = bindingFactory;
		Language = language;
		ProjectFileExtension = projectFileExtension;
		TypeGuid = typeGuid;
		CodeFileExtensions = codeFileExtensions;
	}

	public IProjectBinding? Binding
	{
		get
		{
			if (_binding is not null)
			{
				return _binding;
			}

			if (_bindingFactory is null)
			{
				return null;
			}

			var created = _bindingFactory();
			if (created is null)
			{
				return null;
			}

			var existing = Interlocked.CompareExchange(ref _binding, created, null);
			return existing ?? created;
		}
	}

	public string ProjectFileExtension { get; }

	public Guid TypeGuid { get; }

	public string Language { get; }

	public string[] CodeFileExtensions { get; }
}
