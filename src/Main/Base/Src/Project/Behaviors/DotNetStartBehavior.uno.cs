using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Project.Converter;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.SharpDevelop.Project
{
    public class DotNetStartBehavior : ProjectBehavior
    {
        public DotNetStartBehavior(CompilableProject project, ProjectBehavior next = null)
            : base(project, next)
        {
        }

        new protected CompilableProject Project
        {
            get { return (CompilableProject)base.Project; }
        }

        public override bool IsStartable
        {
            get { return false; }
        }

        public override ProcessStartInfo CreateStartInfo()
        {
            throw new NotSupportedException();
        }

        public override IEnumerable<CompilerVersion> GetAvailableCompilerVersions()
        {
            yield return CompilerVersion.MSBuild80;
            yield return CompilerVersion.MSBuild100;
            yield return CompilerVersion.MSBuild140;
        }

        public override CompilerVersion CurrentCompilerVersion
        {
            get { return CompilerVersion.MSBuild140; }
        }

        public override TargetFramework CurrentTargetFramework
        {
            get { return TargetFramework.Net48; }
        }

        public override IEnumerable<TargetFramework> GetAvailableTargetFrameworks()
        {
            yield return TargetFramework.Net48;
            yield return TargetFramework.Net472;
            yield return TargetFramework.Net462;
            yield return TargetFramework.Net452;
            yield return TargetFramework.Net40;
        }
    }
}
