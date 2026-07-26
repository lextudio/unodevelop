using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Xml.Linq;

using ICSharpCode.Core;
using ICSharpCode.TypeSystem;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Project.Converter;

namespace ICSharpCode.SharpDevelop.Project
{
    public enum OutputType
    {
        [Description("${res:Dialog.Options.PrjOptions.Configuration.CompileTarget.Exe}")]
        Exe,
        [Description("${res:Dialog.Options.PrjOptions.Configuration.CompileTarget.WinExe}")]
        WinExe,
        [Description("${res:Dialog.Options.PrjOptions.Configuration.CompileTarget.Library}")]
        Library,
        [Description("${res:Dialog.Options.PrjOptions.Configuration.CompileTarget.Module}")]
        Module
    }

    public abstract class CompilableProject : MSBuildBasedProject, IUpgradableProject
    {
        protected readonly ISet<string> reparseReferencesSensitiveProperties = new SortedSet<string>();
        protected readonly ISet<string> reparseCodeSensitiveProperties = new SortedSet<string>();

        protected CompilableProject(ProjectLoadInformation information) : base(information)
        {
        }

        protected CompilableProject(ProjectCreateInformation information) : base(information)
        {
        }

        protected override ProjectBehavior CreateDefaultBehavior()
        {
            return new DotNetStartBehavior(this, base.CreateDefaultBehavior());
        }

        public string TargetFrameworkVersion
        {
            get { return GetEvaluatedProperty("TargetFrameworkVersion") ?? "v2.0"; }
            set { SetProperty("TargetFrameworkVersion", value); }
        }

        public string TargetFrameworkProfile
        {
            get { return GetEvaluatedProperty("TargetFrameworkProfile"); }
            set { SetProperty("TargetFrameworkProfile", value); }
        }

        public override string AssemblyName
        {
            get
            {
                string name = base.AssemblyName;
                if (string.IsNullOrEmpty(name))
                {
                    name = Path.GetFileNameWithoutExtension(FileName);
                }
                return name;
            }
        }

        public virtual OutputType OutputType
        {
            get
            {
                return (OutputType)Enum.Parse(typeof(OutputType), GetEvaluatedProperty("OutputType") ?? "Exe", true);
            }
            set
            {
                SetProperty("OutputType", value.ToString());
            }
        }

        public override ICSharpCode.Core.FileName OutputAssemblyFullPath
        {
            get { return null; }
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        #region IUpgradableProject

        public virtual CompilerVersion CurrentCompilerVersion
        {
            get { return GetOrCreateBehavior().CurrentCompilerVersion; }
        }

        public virtual TargetFramework CurrentTargetFramework
        {
            get { return GetOrCreateBehavior().CurrentTargetFramework; }
        }

        public virtual IEnumerable<CompilerVersion> GetAvailableCompilerVersions()
        {
            return GetOrCreateBehavior().GetAvailableCompilerVersions();
        }

        public virtual IEnumerable<TargetFramework> GetAvailableTargetFrameworks()
        {
            return GetOrCreateBehavior().GetAvailableTargetFrameworks();
        }

        bool IUpgradableProject.UpgradeDesired
        {
            get { return false; }
        }

        void IUpgradableProject.UpgradeProject(CompilerVersion newVersion, TargetFramework newFramework)
        {
        }

        #endregion

        #region Type System

        volatile ProjectContentContainer projectContentContainer;
        IUpdateableAssemblyModel assemblyModel;

        protected void InitializeProjectContent(IProjectContent initialProjectContent)
        {
            lock (SyncRoot)
            {
                if (projectContentContainer != null)
                    throw new InvalidOperationException("Already initialized.");
                projectContentContainer = new ProjectContentContainer(this, initialProjectContent);
                projectContentContainer.SetCompilerSettings(CreateCompilerSettings());
            }
        }

        protected virtual object CreateCompilerSettings()
        {
            return null;
        }

        public override IProjectContent ProjectContent
        {
            get
            {
                var c = projectContentContainer;
                return c != null ? c.ProjectContent : null;
            }
        }

        public override IAssemblyModel AssemblyModel
        {
            get
            {
                if (assemblyModel == null)
                {
                    assemblyModel = (IUpdateableAssemblyModel)EmptyAssemblyModel.Instance;
                }
                return assemblyModel;
            }
        }

        public override void OnParseInformationUpdated(ParseInformationEventArgs args)
        {
            var c = projectContentContainer;
            if (c != null)
                c.ParseInformationUpdated(args.OldUnresolvedFile, args.NewUnresolvedFile);
        }

        public override event EventHandler<ParseInformationEventArgs> ParseInformationUpdated = delegate { };

        #endregion
    }
}
