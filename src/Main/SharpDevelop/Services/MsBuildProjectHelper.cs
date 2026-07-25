using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ICSharpCode.Core;

namespace UnoDevelop.Services;

public static class MsBuildProjectHelper
{
    public static string? GetProperty(string projectPath, string propertyName)
    {
        if (!File.Exists(projectPath))
            return null;

        var doc = XDocument.Load(projectPath);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        // Search all property groups for the property
        var prop = doc.Descendants(ns + propertyName).FirstOrDefault();
        return prop?.Value;
    }

    public static bool SetProperty(string projectPath, string propertyName, string value)
    {
        if (!File.Exists(projectPath))
            return false;

        var doc = XDocument.Load(projectPath);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        var existing = doc.Descendants(ns + propertyName).FirstOrDefault();
        if (existing != null)
        {
            existing.Value = value;
        }
        else
        {
            // Find the first property group or create one
            var firstPropertyGroup = doc.Descendants(ns + "PropertyGroup").FirstOrDefault();
            if (firstPropertyGroup == null)
            {
                firstPropertyGroup = new XElement(ns + "PropertyGroup");
                doc.Root?.AddFirst(firstPropertyGroup);
            }
            firstPropertyGroup.Add(new XElement(ns + propertyName, value));
        }

        doc.Save(projectPath);
        return true;
    }

    public static string? GetTargetFramework(string projectPath)
    {
        return GetProperty(projectPath, "TargetFramework")
            ?? GetProperty(projectPath, "TargetFrameworks")
            ?? GetProperty(projectPath, "TargetFrameworkVersion");
    }

    public static bool SetTargetFramework(string projectPath, string tfm)
    {
        // Modern SDK-style projects use TargetFramework
        var targetFramework = GetProperty(projectPath, "TargetFramework");
        if (targetFramework != null)
            return SetProperty(projectPath, "TargetFramework", tfm);

        // Multi-targeting uses TargetFrameworks
        var targetFrameworks = GetProperty(projectPath, "TargetFrameworks");
        if (targetFrameworks != null)
            return SetProperty(projectPath, "TargetFrameworks", tfm);

        // Old style uses TargetFrameworkVersion
        var tfVersion = GetProperty(projectPath, "TargetFrameworkVersion");
        if (tfVersion != null)
            return SetProperty(projectPath, "TargetFrameworkVersion", tfm);

        // No existing target framework property — add one
        return SetProperty(projectPath, "TargetFramework", tfm);
    }
}
