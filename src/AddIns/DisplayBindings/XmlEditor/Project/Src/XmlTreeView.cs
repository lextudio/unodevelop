using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.WinForms;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.XmlEditor;

public class XmlTreeView : IViewContent, IClipboardHandler
{
    readonly XmlTreeViewContainerControl _container;
    bool _ignoreDirty;

    public IViewContent PrimaryViewContent { get; }

    public XmlTreeView(IViewContent primaryViewContent, XmlSchemaCompletionCollection schemas, XmlSchemaCompletion defaultSchema)
    {
        PrimaryViewContent = primaryViewContent ?? throw new ArgumentNullException(nameof(primaryViewContent));
        PrimaryFile = primaryViewContent.PrimaryFile ?? throw new ArgumentNullException("PrimaryViewContent.PrimaryFile");
        TabPageText = "${res:ICSharpCode.XmlEditor.XmlTreeView.Title}";
        _container = new XmlTreeViewContainerControl(schemas, defaultSchema);
        _container.DirtyChanged += OnContainerDirtyChanged;
    }

    public bool EnableCut => _container.EnableCut;
    public bool EnableCopy => _container.EnableCopy;
    public bool EnablePaste => _container.EnablePaste;
    public bool EnableDelete => _container.EnableDelete;
    public bool EnableSelectAll => false;

    public void Cut() => _container.Cut();
    public void Copy() => _container.Copy();
    public void Paste() => _container.Paste();
    public void Delete() => _container.Delete();
    public void SelectAll() { }

    public object Control => _container;
    public object InitiallyFocusedControl => null;
    public IWorkbenchWindow WorkbenchWindow { get; set; }
    public event EventHandler TabPageTextChanged;
    public string TabPageText { get; set; }
    public string TitleName { get; set; }
    public event EventHandler TitleNameChanged;
    public string InfoTip => string.Empty;
    public event EventHandler InfoTipChanged;
    public bool IsDirty { get => _container.IsDirty; set => _container.IsDirty = value; }
    public event EventHandler IsDirtyChanged;

    public void Save(OpenedFile file, Stream stream)
    {
        if (file != PrimaryFile)
            throw new ArgumentException("file must be the primary file");
        SaveToPrimary();
        PrimaryViewContent.Save(file, stream);
    }

    public void Load(OpenedFile file, Stream stream)
    {
        if (file != PrimaryFile)
            throw new ArgumentException("file must be the primary file");
        PrimaryViewContent.Load(file, stream);
        LoadFromPrimary();
    }

    public IList<OpenedFile> Files { get; } = new List<OpenedFile>();
    public OpenedFile PrimaryFile { get; }
    public FileName PrimaryFileName => PrimaryFile?.FileName;
    public INavigationPoint BuildNavPoint() => null;
    public bool IsDisposed { get; private set; }
    public event EventHandler Disposed;
    public bool IsReadOnly => false;
    public bool IsViewOnly => false;
    public bool CloseWithSolution => false;
    public ICollection<IViewContent> SecondaryViewContents => PrimaryViewContent.SecondaryViewContents;

    public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView)
        => file == PrimaryFile && newView.SupportsSwitchToThisWithoutSaveLoad(file, PrimaryViewContent);

    public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView)
        => file == PrimaryFile && PrimaryViewContent.SupportsSwitchToThisWithoutSaveLoad(file, oldView);

    public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView)
    {
        if (file == PrimaryFile && this != newView)
        {
            SaveToPrimary();
            PrimaryViewContent.SwitchFromThisWithoutSaveLoad(file, newView);
        }
    }

    public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView)
    {
        if (file == PrimaryFile && oldView != this)
        {
            PrimaryViewContent.SwitchToThisWithoutSaveLoad(file, oldView);
            LoadFromPrimary();
        }
    }

    void LoadFromPrimary()
    {
        var provider = ((IFileDocumentProvider)PrimaryViewContent);
        var doc = provider.GetDocumentForFile(PrimaryFile);
        _container.LoadXml(doc.Text);
        var view = XmlView.ForFile(PrimaryFile);
        XmlView.CheckIsWellFormed(view.TextEditor);
    }

    void SaveToPrimary()
    {
        if (!_container.IsErrorMessageTextBoxVisible && _container.IsDirty)
        {
            var view = XmlView.ForFile(PrimaryFile);
            if (view != null)
            {
                XmlView.ReplaceAll(_container.Document.OuterXml, view.TextEditor);
                _ignoreDirty = true;
                _container.IsDirty = false;
                _ignoreDirty = false;
            }
        }
    }

    void OnContainerDirtyChanged(object source, EventArgs e)
    {
        if (!_ignoreDirty)
            IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        LoggingService.Debug("XmlTreeView.Dispose");
        if (!IsDisposed)
        {
            IsDisposed = true;
            Disposed?.Invoke(this, EventArgs.Empty);
        }
    }

    public object GetService(Type serviceType) => null;
}
