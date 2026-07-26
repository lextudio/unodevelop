using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;

namespace UnoDevelop.Workbench;

internal sealed class LayoutConfiguration
{
    const string ConfigFile = "LayoutConfig.xml";

    public static readonly List<LayoutConfiguration> Layouts = new();

    public static string DataLayoutPath
        => Path.Combine(SD.PropertyService.DataDirectory, "layouts");

    public static string ConfigLayoutPath
        => Path.Combine(SD.PropertyService.ConfigDirectory, "layouts");

    const string DefaultLayoutName = "Default";

    static LayoutConfiguration()
    {
        LoadLayoutConfiguration();
    }

    string name;
    string fileName;
    string displayName;
    bool readOnly;
    bool custom;

    public bool Custom
    {
        get => custom;
        set => custom = value;
    }

    public string FileName
    {
        get => fileName;
        set => fileName = value;
    }

    public string Name
    {
        get => name;
        set => name = value;
    }

    public string DisplayName
        => displayName == null ? Name : StringParser.Parse(displayName);

    public bool ReadOnly
    {
        get => readOnly;
        set => readOnly = value;
    }

    LayoutConfiguration() { }

    LayoutConfiguration(XmlElement el, bool custom)
    {
        name = el.GetAttribute("name");
        fileName = el.GetAttribute("file");
        readOnly = bool.Parse(el.GetAttribute("readonly"));
        if (el.HasAttribute("displayName"))
            displayName = el.GetAttribute("displayName");
        this.custom = custom;
    }

    public static LayoutConfiguration CreateCustom(string name)
    {
        var l = new LayoutConfiguration
        {
            name = name,
            fileName = Path.GetRandomFileName() + ".xml",
            custom = true,
        };
        var src = Path.Combine(DataLayoutPath, "Default.xml");
        var dst = Path.Combine(ConfigLayoutPath, l.fileName);
        if (File.Exists(src))
            File.Copy(src, dst);
        Layouts.Add(l);
        return l;
    }

    public override string ToString() => DisplayName;

    static string currentLayoutName = DefaultLayoutName;

    public static string CurrentLayoutName
    {
        get => currentLayoutName;
        set
        {
            SD.MainThread.VerifyAccess();
            if (value != CurrentLayoutName)
            {
                SD.Workbench.CurrentLayoutConfiguration = value;
                currentLayoutName = value;
                OnLayoutChanged(EventArgs.Empty);
            }
        }
    }

    public static void ReloadDefaultLayout()
    {
        currentLayoutName = DefaultLayoutName;
        SD.Workbench.CurrentLayoutConfiguration = DefaultLayoutName;
        OnLayoutChanged(EventArgs.Empty);
    }

    public static string? CurrentLayoutFileName
    {
        get
        {
            var current = CurrentLayout;
            return current != null ? Path.Combine(ConfigLayoutPath, current.FileName) : null;
        }
    }

    public static string? CurrentLayoutTemplateFileName
    {
        get
        {
            var current = CurrentLayout;
            return current != null ? Path.Combine(DataLayoutPath, current.FileName) : null;
        }
    }

    public static LayoutConfiguration? CurrentLayout
    {
        get
        {
            foreach (var config in Layouts)
            {
                if (config.name == CurrentLayoutName)
                    return config;
            }
            return null;
        }
    }

    public static LayoutConfiguration? GetLayout(string name)
    {
        foreach (var config in Layouts)
        {
            if (config.Name == name)
                return config;
        }
        return null;
    }

    internal static void LoadLayoutConfiguration()
    {
        Layouts.Clear();
        var configPath = ConfigLayoutPath;
        if (File.Exists(Path.Combine(configPath, ConfigFile)))
            LoadLayoutConfiguration(Path.Combine(configPath, ConfigFile), true);

        var dataPath = DataLayoutPath;
        if (File.Exists(Path.Combine(dataPath, ConfigFile)))
            LoadLayoutConfiguration(Path.Combine(dataPath, ConfigFile), false);
    }

    static void LoadLayoutConfiguration(string layoutConfig, bool custom)
    {
        var doc = new XmlDocument();
        doc.Load(layoutConfig);
        foreach (XmlElement el in doc.DocumentElement.ChildNodes)
            Layouts.Add(new LayoutConfiguration(el, custom));
    }

    public static void SaveCustomLayoutConfiguration()
    {
        var configPath = ConfigLayoutPath;
        Directory.CreateDirectory(configPath);
        using var w = new XmlTextWriter(Path.Combine(configPath, ConfigFile), System.Text.Encoding.UTF8)
        {
            Formatting = Formatting.Indented,
        };
        w.WriteStartElement("LayoutConfig");
        foreach (var lc in Layouts)
        {
            if (lc.custom)
            {
                w.WriteStartElement("Layout");
                w.WriteAttributeString("name", lc.name);
                w.WriteAttributeString("file", lc.fileName);
                w.WriteAttributeString("readonly", lc.readOnly.ToString());
                if (lc.displayName != null)
                    w.WriteAttributeString("displayName", lc.displayName);
                w.WriteEndElement();
            }
        }
        w.WriteEndElement();
    }

    static void OnLayoutChanged(EventArgs e)
        => LayoutChanged?.Invoke(null, e);

    public static event EventHandler? LayoutChanged;
}
