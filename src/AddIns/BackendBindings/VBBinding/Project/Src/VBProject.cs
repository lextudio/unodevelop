using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;

namespace ICSharpCode.VBBinding
{
    public class VBProject : CompilableProject
    {
        public const string DefaultTargetsFile = "$(MSBuildToolsPath)\\Microsoft.VisualBasic.targets";

        public VBProject(ProjectLoadInformation info) : base(info)
        {
            InitVB();
        }

        public VBProject(ProjectCreateInformation info) : base(info)
        {
            InitVB();
            AddImport(DefaultTargetsFile, null);
            SetProperty("Debug", null, "DefineConstants", "DEBUG=1,TRACE=1", PropertyStorageLocations.ConfigurationSpecific, true);
            SetProperty("Release", null, "DefineConstants", "TRACE=1", PropertyStorageLocations.ConfigurationSpecific, true);
        }

        protected override void OnPropertyChanged(ProjectPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.PropertyName == "OutputType")
            {
                switch (OutputType)
                {
                    case OutputType.WinExe:
                        SetProperty(e.Configuration, e.Platform, "MyType", "WindowsForms", e.NewLocation, true);
                        break;
                    case OutputType.Exe:
                        SetProperty(e.Configuration, e.Platform, "MyType", "Console", e.NewLocation, true);
                        break;
                    default:
                        SetProperty(e.Configuration, e.Platform, "MyType", "Windows", e.NewLocation, true);
                        break;
                }
            }
        }

        public override string Language
        {
            get { return VBProjectBinding.LanguageName; }
        }

        public override Task<bool> BuildAsync(ProjectBuildOptions options, IBuildFeedbackSink feedbackSink, IProgressMonitor progressMonitor)
        {
            return base.BuildAsync(options, feedbackSink, progressMonitor);
        }

        public override IEnumerable<ReferenceProjectItem> ResolveAssemblyReferences(CancellationToken cancellationToken)
        {
            var additionalItems = new ReferenceProjectItem[]
            {
                new ReferenceProjectItem(this, "mscorlib"),
                new ReferenceProjectItem(this, "Microsoft.VisualBasic")
            };
            return SD.MSBuildEngine.ResolveAssemblyReferences(this, additionalItems);
        }

        public override string GetDefaultNamespace(string fileName)
        {
            return RootNamespace;
        }

        public override CodeDomProvider CreateCodeDomProvider()
        {
            return new Microsoft.VisualBasic.VBCodeProvider();
        }

        protected override ProjectBehavior CreateDefaultBehavior()
        {
            return new VBProjectBehavior(this, base.CreateDefaultBehavior());
        }

        void InitVB()
        {
            reparseReferencesSensitiveProperties.Add("TargetFrameworkVersion");
            reparseCodeSensitiveProperties.Add("DefineConstants");
        }

        public bool? OptionInfer
        {
            get { return GetValue("OptionInfer", false); }
        }

        public bool? OptionStrict
        {
            get { return GetValue("OptionStrict", false); }
        }

        public bool? OptionExplicit
        {
            get { return GetValue("OptionExplicit", true); }
        }

        public CompareKind? OptionCompare
        {
            get
            {
                string val = GetEvaluatedProperty("OptionCompare");
                if ("Text".Equals(val, StringComparison.OrdinalIgnoreCase))
                    return CompareKind.Text;
                return CompareKind.Binary;
            }
        }

        bool? GetValue(string name, bool defaultValue)
        {
            string val;
            try
            {
                val = GetEvaluatedProperty(name);
            }
            catch (ObjectDisposedException)
            {
                val = null;
            }
            if (val == null)
                return defaultValue;
            return "On".Equals(val, StringComparison.OrdinalIgnoreCase);
        }
    }
}
