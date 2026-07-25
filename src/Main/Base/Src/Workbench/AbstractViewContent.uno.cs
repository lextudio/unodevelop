using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.Core;

namespace ICSharpCode.SharpDevelop.Workbench
{
	public abstract class AbstractViewContent : IViewContent
	{
		public AbstractViewContent() { }
		public AbstractViewContent(OpenedFile file) { PrimaryFile = file; }
		public abstract object? Control { get; }
		public virtual object? InitiallyFocusedControl => null;
		public IWorkbenchWindow? WorkbenchWindow { get; set; }
		public event EventHandler? TabPageTextChanged;
		public string TabPageText { get => string.Empty; set { } }
		public string TitleName { get => string.Empty; set { } }
		public event EventHandler? TitleNameChanged;
		public string InfoTip => string.Empty;
		public event EventHandler? InfoTipChanged;
		public bool IsDirty { get => false; set { } }
		public event EventHandler? IsDirtyChanged;
		public virtual void Save(OpenedFile file, Stream stream) { }
		public virtual void Load(OpenedFile file, Stream stream) { }
		public IList<OpenedFile> Files => Array.Empty<OpenedFile>();
		public OpenedFile? PrimaryFile { get; protected set; }
		public FileName? PrimaryFileName => PrimaryFile?.FileName;
		public INavigationPoint BuildNavPoint() => null!;
		public bool IsDisposed => false;
		public event EventHandler? Disposed;
		public bool IsReadOnly => false;
		public bool IsViewOnly => false;
		public bool CloseWithSolution => false;
		public ICollection<IViewContent> SecondaryViewContents => Array.Empty<IViewContent>();
		public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) => false;
		public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) => false;
		public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) { }
		public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) { }
		public void Dispose() { }
		public IServiceProvider? ServiceProvider => null;
		public object? GetService(Type serviceType) => null;
	}
}
