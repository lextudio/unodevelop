// Extracted from upstream SharpDevelop's full FileUtility.cs (see
// externals/SharpDevelop/src/Main/Core/Project/Src/Services/FileUtility/FileUtility.cs) — only
// ObservedSave/FileSaved/FileErrorPolicy, none of which have any WPF dependency. The rest of that
// file overlaps with FileUtility.uno.cs's own Uno-native implementations (GetRelativePath,
// IsValidPath, etc.), so it isn't linked wholesale — this is the portable slice CustomTool.cs
// (docs/t4-templating.md) needs that the trimmed FileUtility.Minimal.cs doesn't have.

using System;

namespace ICSharpCode.Core
{
    public enum FileErrorPolicy
    {
        Inform,
        ProvideAlternative
    }

    public enum FileOperationResult
    {
        OK,
        Failed,
        SavedAlternatively
    }

    public delegate void FileOperationDelegate();

    public delegate void NamedFileOperationDelegate(FileName fileName);

    static partial class FileUtility
    {
        public static event EventHandler<FileNameEventArgs>? FileSaved;

        public static void RaiseFileSaved(FileNameEventArgs e)
        {
            FileSaved?.Invoke(null, e);
        }

        public static FileOperationResult ObservedSave(FileOperationDelegate saveFile, FileName fileName, string message, FileErrorPolicy policy = FileErrorPolicy.Inform)
        {
            try
            {
                saveFile();
                RaiseFileSaved(new FileNameEventArgs(fileName));
                return FileOperationResult.OK;
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                return ObservedSaveHandleException(e, saveFile, fileName, message, policy);
            }
        }

        public static FileOperationResult ObservedSave(FileOperationDelegate saveFile, FileName fileName, FileErrorPolicy policy = FileErrorPolicy.Inform)
        {
            return ObservedSave(saveFile, fileName, ResourceService.GetString("ICSharpCode.Services.FileUtilityService.CantSaveFileStandardText"), policy);
        }

        static FileOperationResult ObservedSaveHandleException(Exception e, FileOperationDelegate saveFile, FileName fileName, string message, FileErrorPolicy policy)
        {
            var messageService = ServiceSingleton.GetRequiredService<IMessageService>();
            switch (policy)
            {
                case FileErrorPolicy.Inform:
                    messageService.InformSaveError(fileName, message, "${res:FileUtilityService.ErrorWhileSaving}", e);
                    break;
                case FileErrorPolicy.ProvideAlternative:
                    var result = messageService.ChooseSaveError(fileName, message, "${res:FileUtilityService.ErrorWhileSaving}", e, false);
                    if (result.IsRetry)
                        return ObservedSave(saveFile, fileName, message, policy);
                    if (result.IsIgnore)
                        return FileOperationResult.Failed;
                    break;
            }

            return FileOperationResult.Failed;
        }

        public static FileOperationResult ObservedSave(NamedFileOperationDelegate saveFileAs, FileName fileName, string message, FileErrorPolicy policy = FileErrorPolicy.Inform)
        {
            try
            {
                System.IO.Directory.CreateDirectory(fileName.GetParentDirectory());
                saveFileAs(fileName);
                RaiseFileSaved(new FileNameEventArgs(fileName));
                return FileOperationResult.OK;
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                return ObservedSaveHandleException(e, saveFileAs, fileName, message, policy);
            }
        }

        public static FileOperationResult ObservedSave(NamedFileOperationDelegate saveFileAs, FileName fileName, FileErrorPolicy policy = FileErrorPolicy.Inform)
        {
            return ObservedSave(saveFileAs, fileName, ResourceService.GetString("ICSharpCode.Services.FileUtilityService.CantSaveFileStandardText"), policy);
        }

        static FileOperationResult ObservedSaveHandleException(Exception e, NamedFileOperationDelegate saveFileAs, FileName fileName, string message, FileErrorPolicy policy)
        {
            var messageService = ServiceSingleton.GetRequiredService<IMessageService>();
            switch (policy)
            {
                case FileErrorPolicy.Inform:
                    messageService.InformSaveError(fileName, message, "${res:FileUtilityService.ErrorWhileSaving}", e);
                    break;
                case FileErrorPolicy.ProvideAlternative:
                    var result = messageService.ChooseSaveError(fileName, message, "${res:FileUtilityService.ErrorWhileSaving}", e, true);
                    if (result.IsRetry)
                        return ObservedSave(saveFileAs, fileName, message, policy);
                    if (result.IsIgnore)
                        return FileOperationResult.Failed;
                    if (result.AlternativeFileName is not null)
                        return ObservedSave(saveFileAs, result.AlternativeFileName, message, policy);
                    break;
            }

            return FileOperationResult.Failed;
        }
    }
}
