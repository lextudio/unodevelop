using System.IO;
using ICSharpCode.SharpDevelop.Project;

namespace ICSharpCode.VBBinding
{
    public class VBProjectBehavior : ProjectBehavior
    {
        public VBProjectBehavior(VBProject project, ProjectBehavior next = null)
            : base(project, next) { }

        public override ItemType GetDefaultItemType(string fileName)
        {
            if (string.Equals(Path.GetExtension(fileName), ".vb", System.StringComparison.OrdinalIgnoreCase))
                return ItemType.Compile;
            return base.GetDefaultItemType(fileName);
        }
    }
}
