using System.IO;
using ICSharpCode.Core;
using ICSharpCode.Core.Implementation;

namespace UnoDevelop.Services;

internal sealed class UnoMessageService : TextWriterMessageService
{
    public UnoMessageService() : base(TextWriter.Synchronized(System.Console.Out))
    {
        ProductName = "UnoDevelop";
        DefaultMessageBoxTitle = "UnoDevelop";
    }
}
