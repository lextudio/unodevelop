using System;
using System.IO;
using ICSharpCode.Core;

namespace ICSharpCode.SharpDevelop.Project;

public enum CopyToOutputDirectory
{
    Never,
    Always,
    PreserveNewest
}

public class FileProjectItem : ProjectItem
{
    public FileProjectItem(IProject project, ItemType itemType, string include)
        : base(project, itemType, include)
    {
    }

    public FileProjectItem(IProject project, ItemType itemType)
        : base(project, itemType)
    {
    }

    public string BuildAction
    {
        get => ItemType.ItemName;
        set => ItemType = new ItemType(value);
    }

    public CopyToOutputDirectory CopyToOutputDirectory
    {
        get
        {
            var value = GetEvaluatedMetadata("CopyToOutputDirectory");
            return Enum.TryParse<CopyToOutputDirectory>(value, ignoreCase: true, out var parsed)
                ? parsed
                : CopyToOutputDirectory.Never;
        }
        set => SetEvaluatedMetadata("CopyToOutputDirectory", value.ToString());
    }

    public string CustomTool
    {
        get => GetEvaluatedMetadata("Generator");
        set => SetEvaluatedMetadata("Generator", value);
    }

    public string DependentUpon
    {
        get => GetEvaluatedMetadata("DependentUpon");
        set => SetEvaluatedMetadata("DependentUpon", value);
    }

    public string SubType
    {
        get => GetEvaluatedMetadata("SubType");
        set => SetEvaluatedMetadata("SubType", value);
    }

    public bool IsLink => HasMetadata("Link") || !FileUtility.IsBaseDirectory(Project.Directory, FileName);

    public string VirtualName
    {
        get
        {
            if (HasMetadata("Link"))
            {
                return GetEvaluatedMetadata("Link");
            }

            if (FileUtility.IsBaseDirectory(Project.Directory, FileName))
            {
                return Include;
            }

            return Path.GetFileName(Include);
        }
    }
}
