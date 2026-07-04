using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.Core;

namespace ICSharpCode.SharpDevelop.Project;

public class ProjectItem
{
    readonly Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase);
    string include = string.Empty;
    FileName? fileNameCache;

    public ProjectItem(IProject project, ItemType itemType, string include = "")
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        ItemType = itemType;
        Include = include ?? string.Empty;
    }

    public IProject Project { get; }

    public ItemType ItemType { get; set; }

    public string Include
    {
        get => include;
        set
        {
            include = value ?? string.Empty;
            fileNameCache = null;
        }
    }

    public virtual FileName FileName
    {
        get
        {
            var cached = fileNameCache;
            if (cached is not null)
            {
                return cached;
            }

            var resolved = FileName.Create(Path.Combine(Project.Directory, Include.Replace('\\', Path.DirectorySeparatorChar)));
            fileNameCache = resolved;
            return resolved;
        }
        set
        {
            var relative = Path.GetRelativePath(Project.Directory, value)
                .Replace(Path.DirectorySeparatorChar, '\\');
            Include = relative;
            fileNameCache = value;
        }
    }

    public bool HasMetadata(string metadataName) => metadata.ContainsKey(metadataName);

    public string GetEvaluatedMetadata(string metadataName) => GetMetadata(metadataName);

    public string GetMetadata(string metadataName)
    {
        return metadata.TryGetValue(metadataName, out var value) ? value : string.Empty;
    }

    public void SetEvaluatedMetadata(string metadataName, string value) => SetMetadata(metadataName, value);

    public void SetMetadata(string metadataName, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            metadata.Remove(metadataName);
            return;
        }

        metadata[metadataName] = value;
    }

    public void RemoveMetadata(string metadataName) => metadata.Remove(metadataName);

    public IEnumerable<string> MetadataNames => metadata.Keys;
}
