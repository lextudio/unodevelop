using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Project;

namespace ICSharpCode.SharpDevelop
{
    public static partial class SD
    {
        public static IParserService ParserService
        {
            get { return GetRequiredService<IParserService>(); }
        }

        public static IMSBuildEngine MSBuildEngine
        {
            get { return GetRequiredService<IMSBuildEngine>(); }
        }
    }
}
