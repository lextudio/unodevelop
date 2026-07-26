using System.IO;
using System.Text;
using ICSharpCode.SharpDevelop.Project;
using Microsoft.Build.Framework;

namespace ICSharpCode.VBBinding
{
    public sealed class VbcEncodingFixingLogger : IMSBuildLoggerFilter
    {
        public IMSBuildChainedLoggerFilter CreateFilter(IMSBuildLoggerContext context, IMSBuildChainedLoggerFilter nextFilter)
        {
            return new VbcLoggerImpl(nextFilter);
        }

        sealed class VbcLoggerImpl : IMSBuildChainedLoggerFilter
        {
            readonly IMSBuildChainedLoggerFilter nextFilter;
            string lastFileName, lastLineText;
            StreamReader lastFile;
            int lastLine;

            public VbcLoggerImpl(IMSBuildChainedLoggerFilter nextFilter)
            {
                this.nextFilter = nextFilter;
            }

        static string FixEncoding(string text)
        {
            if (text == null) return text;
            return Encoding.Default.GetString(Encoding.Default.GetBytes(text));
        }

            public void HandleError(BuildError error)
            {
                error.ErrorText = FixEncoding(error.ErrorText);
                error.FileName = FixEncoding(error.FileName);
                error.Column = FixColumn(error.FileName, error.Line, error.Column);
                nextFilter.HandleError(error);
            }

            public void HandleBuildEvent(Microsoft.Build.Framework.BuildEventArgs e)
            {
                nextFilter.HandleBuildEvent(e);
                if (e is Microsoft.Build.Framework.TaskFinishedEventArgs && lastFile != null)
                {
                    lastFile.Close();
                    lastFile = null;
                }
            }

            int FixColumn(string fileName, int line, int column)
            {
                if (!File.Exists(fileName) || line < 1 || column < 1)
                    return column;

                if (fileName != lastFileName || line < lastLine || lastFile == null)
                {
                    if (lastFile != null)
                        lastFile.Close();
                    lastFile = new StreamReader(fileName);
                    lastFileName = fileName;
                    lastLineText = "";
                    lastLine = 0;
                }

                while (lastLine < line && lastLineText != null)
                {
                    lastLineText = lastFile.ReadLine();
                    lastLine++;
                }

                if (!string.IsNullOrEmpty(lastLineText))
                {
                    int i = 0;
                    while (i < column && i < lastLineText.Length)
                    {
                        if (lastLineText[i] == '\t')
                            column -= 3;
                        i++;
                    }
                }

                return column;
            }
        }
    }
}
