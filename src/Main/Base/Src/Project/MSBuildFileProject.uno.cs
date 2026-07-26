using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml;

namespace ICSharpCode.SharpDevelop.Project
{
    public class MSBuildFileProject : AbstractProject
    {
        SolutionFormatVersion minimumSolutionVersion = SolutionFormatVersion.VS2019;

        public MSBuildFileProject(ProjectLoadInformation information) : base(information)
        {
            try
            {
                using (XmlReader r = XmlReader.Create(information.FileName, new XmlReaderSettings { IgnoreComments = true, XmlResolver = null }))
                {
                    if (r.Read() && r.MoveToContent() == XmlNodeType.Element)
                    {
                        string toolsVersion = r.GetAttribute("ToolsVersion");
                        if (string.IsNullOrEmpty(toolsVersion) || toolsVersion.Equals("Current", StringComparison.OrdinalIgnoreCase))
                            minimumSolutionVersion = SolutionFormatVersion.VS2026;
                    }
                }
            }
            catch (XmlException ex)
            {
                throw new ProjectLoadException(ex.Message, ex);
            }
        }

        public override SolutionFormatVersion MinimumSolutionVersion
        {
            get { return minimumSolutionVersion; }
        }

        public override Task<bool> BuildAsync(ProjectBuildOptions options, IBuildFeedbackSink feedbackSink, IProgressMonitor progressMonitor)
        {
            throw new NotSupportedException();
        }
    }
}
