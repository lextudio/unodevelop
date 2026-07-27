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
            yield return CompilerVersion.MSBuild20;
            yield return CompilerVersion.MSBuild35;
            yield return CompilerVersion.MSBuild40;
            yield return CompilerVersion.MSBuild80;
            yield return CompilerVersion.MSBuild100;
            yield return CompilerVersion.MSBuild120;
            yield return CompilerVersion.MSBuild140;
            yield return CompilerVersion.MSBuild150;
            yield return CompilerVersion.MSBuild160;
            yield return CompilerVersion.MSBuild170;
            yield return CompilerVersion.MSBuild180;
        }

        public override CompilerVersion CurrentCompilerVersion
        {
            get { return CompilerVersion.MSBuild180; }
        }

        public override TargetFramework CurrentTargetFramework
        {
            get { return TargetFramework.Net481; }
        }

        public override IEnumerable<TargetFramework> GetAvailableTargetFrameworks()
        {
            yield return TargetFramework.Net481;
            yield return TargetFramework.Net48;
            yield return TargetFramework.Net472;
            yield return TargetFramework.Net471;
            yield return TargetFramework.Net47;
            yield return TargetFramework.Net462;
            yield return TargetFramework.Net461;
            yield return TargetFramework.Net46;
            yield return TargetFramework.Net452;
            yield return TargetFramework.Net451;
            yield return TargetFramework.Net45;
            yield return TargetFramework.Net40;
        }
    }
}
