using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml;

namespace ICSharpCode.Core;

public interface IMementoCapable
{
    Properties CreateMemento();

    void SetMemento(Properties memento);
}

public sealed class Properties : INotifyPropertyChanged, ICloneable
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsDirty { get; set; }

    public IReadOnlyList<string> Keys => _values.Keys.ToArray();

    public string this[string key]
    {
        get => Get(key, string.Empty);
        set => Set(key, value);
    }

    public bool Contains(string key)
    {
        return _values.ContainsKey(key);
    }

    public T Get<T>(string key, T defaultValue)
    {
        if (!_values.TryGetValue(key, out var raw) || raw is null)
        {
            return defaultValue;
        }

        if (raw is T typed)
        {
            return typed;
        }

        try
        {
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            // Enums are stored as their string name in the AddIn tree (e.g. a condition's
            // action="Disable"). Convert.ChangeType cannot parse enum-from-string and would throw,
            // silently falling back to the default — which turned every conditioned toolbar/menu
            // item into action="Exclude" (removed) instead of "Disable". Handle enums explicitly.
            if (targetType.IsEnum)
            {
                return raw is string enumName
                    ? (T)Enum.Parse(targetType, enumName, ignoreCase: true)
                    : (T)Enum.ToObject(targetType, raw);
            }

            return (T)Convert.ChangeType(raw, targetType);
        }
        catch
        {
            return defaultValue;
        }
    }

    public void Set<T>(string key, T value)
    {
        if (value is null)
        {
            Remove(key);
            return;
        }

        _values[key] = value;
        IsDirty = true;
        OnPropertyChanged(key);
    }

    public IReadOnlyList<T> GetList<T>(string key)
    {
        if (!_values.TryGetValue(key, out var raw) || raw is null)
        {
            return Array.Empty<T>();
        }

        if (raw is IReadOnlyList<T> ro)
        {
            return ro;
        }

        if (raw is IEnumerable<T> seq)
        {
            return seq.ToArray();
        }

        // Lists are stored as string[] (see SetList); convert element-wise to T.
        if (raw is System.Collections.IEnumerable enumerable and not string)
        {
            var list = new List<T>();
            foreach (var item in enumerable)
            {
                if (item is T typed)
                {
                    list.Add(typed);
                }
                else if (item is not null)
                {
                    try { list.Add((T)Convert.ChangeType(item, typeof(T))); }
                    catch { }
                }
            }

            return list;
        }

        return Array.Empty<T>();
    }

    public void SetList<T>(string key, IEnumerable<T> value)
    {
        // Store as string[] so the value round-trips through disk regardless of T
        // (e.g. RecentOpen writes FileName[] but reads back string[]).
        var data = value?
            .Select(v => v?.ToString())
            .Where(v => v is not null)
            .Cast<string>()
            .ToArray() ?? Array.Empty<string>();
        if (data.Length == 0)
        {
            Remove(key);
            return;
        }

        _values[key] = data;
        IsDirty = true;
        OnPropertyChanged(key);
    }

    public Properties NestedProperties(string key)
    {
        if (_values.TryGetValue(key, out var raw) && raw is Properties nested)
        {
            return nested;
        }

        var created = new Properties();
        _values[key] = created;
        IsDirty = true;
        OnPropertyChanged(key);
        return created;
    }

    public void SetNestedProperties(string key, Properties nestedProperties)
    {
        if (nestedProperties is null)
        {
            Remove(key);
            return;
        }

        _values[key] = nestedProperties;
        IsDirty = true;
        OnPropertyChanged(key);
    }

    public void Remove(string key)
    {
        if (_values.Remove(key))
        {
            IsDirty = true;
            OnPropertyChanged(key);
        }
    }

    public object Clone()
    {
        var clone = new Properties();
        foreach (var pair in _values)
        {
            clone._values[pair.Key] = pair.Value;
        }

        clone.IsDirty = IsDirty;
        return clone;
    }

    internal static Properties ReadFromAttributes(XmlReader reader)
    {
        var properties = new Properties();
        if (!reader.HasAttributes)
        {
            return properties;
        }

        while (reader.MoveToNextAttribute())
        {
            properties[reader.Name] = reader.Value;
        }

        reader.MoveToElement();
        return properties;
    }

    private void OnPropertyChanged(string key)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
    }

    public static Properties Load(ICSharpCode.Core.FileName fileName)
    {
        try
        {
            var root = System.Xml.Linq.XDocument.Load((string)fileName).Root;
            return root is null ? new Properties() : LoadContents(root);
        }
        catch
        {
            return new Properties();
        }
    }

    private static Properties LoadContents(System.Xml.Linq.XElement element)
    {
        var props = new Properties();
        foreach (var child in element.Elements())
        {
            var key = (string?)child.Attribute("key");
            if (key is null)
            {
                continue;
            }

            switch (child.Name.LocalName)
            {
                case "Property":
                    props._values[key] = (string?)child.Attribute("value") ?? child.Value;
                    break;
                case "Array":
                    props._values[key] = child.Elements("Element")
                        .Select(e => e.Value)
                        .ToArray();
                    break;
                case "Properties":
                    props._values[key] = LoadContents(child);
                    break;
            }
        }

        return props;
    }

    public void Save(ICSharpCode.Core.FileName fileName)
    {
        try
        {
            new System.Xml.Linq.XDocument(SaveContents()).Save((string)fileName);
        }
        catch { }
    }

    private System.Xml.Linq.XElement SaveContents()
    {
        var root = new System.Xml.Linq.XElement("Properties");
        foreach (var pair in _values)
        {
            var key = new System.Xml.Linq.XAttribute("key", pair.Key);
            switch (pair.Value)
            {
                case Properties nested:
                    var nestedElement = nested.SaveContents();
                    nestedElement.Add(key);
                    root.Add(nestedElement);
                    break;
                case string s:
                    root.Add(new System.Xml.Linq.XElement("Property", key,
                        new System.Xml.Linq.XAttribute("value", s)));
                    break;
                case System.Collections.IEnumerable enumerable when pair.Value is not string:
                    var arrayElement = new System.Xml.Linq.XElement("Array", key);
                    foreach (var item in enumerable)
                    {
                        arrayElement.Add(new System.Xml.Linq.XElement("Element", item?.ToString() ?? string.Empty));
                    }

                    root.Add(arrayElement);
                    break;
                case not null:
                    root.Add(new System.Xml.Linq.XElement("Property", key,
                        new System.Xml.Linq.XAttribute("value", pair.Value.ToString() ?? string.Empty)));
                    break;
            }
        }

        return root;
    }
}
