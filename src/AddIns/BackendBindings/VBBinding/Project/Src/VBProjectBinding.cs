using ICSharpCode.SharpDevelop.Project;

namespace ICSharpCode.VBBinding
{
    public class VBProjectBinding : IProjectBinding
    {
        public const string LanguageName = "VB";

        public IProject LoadProject(ProjectLoadInformation info)
        {
            return new VBProject(info);
        }

        public IProject CreateProject(ProjectCreateInformation info)
        {
            return new VBProject(info);
        }

        public bool HandlingMissingProject
        {
            get { return false; }
        }
    }
}
