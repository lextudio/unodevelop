using System;
using System.Globalization;
using System.Text;

namespace ICSharpCode.Core;

public static class StringParser
{
    public static string Escape(string input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        return input.Replace("${", "${$}{", StringComparison.Ordinal);
    }

    public static string Parse(string input)
    {
        return Parse(input, (StringTagPair[])null!);
    }

    public static string Parse(string input, params StringTagPair[] customTags)
    {
        if (input is null)
        {
            return null!;
        }

        var pos = 0;
        StringBuilder output = null;
        do
        {
            var oldPos = pos;
            pos = input.IndexOf("${", pos, StringComparison.Ordinal);
            if (pos < 0)
            {
                if (output is null)
                {
                    return input;
                }

                if (oldPos < input.Length)
                {
                    output.Append(input, oldPos, input.Length - oldPos);
                }

                return output.ToString();
            }

            output ??= pos == 0 ? new StringBuilder() : new StringBuilder(input, 0, pos, pos + 16);
            if (pos > oldPos)
            {
                output.Append(input, oldPos, pos - oldPos);
            }

            var end = input.IndexOf('}', pos + 1);
            if (end < 0)
            {
                output.Append("${");
                pos += 2;
                continue;
            }

            var property = input.Substring(pos + 2, end - pos - 2);
            var val = GetValue(property, customTags);
            if (val is null)
            {
                output.Append("${");
                output.Append(property);
                output.Append('}');
            }
            else
            {
                output.Append(val);
            }

            pos = end + 1;
        }
        while (pos < input.Length);

        return output.ToString();
    }

    public static string GetValue(string propertyName, params StringTagPair[] customTags)
    {
        if (propertyName is null)
        {
            throw new ArgumentNullException(nameof(propertyName));
        }

        if (propertyName == "$")
        {
            return "$";
        }

        if (customTags is not null)
        {
            foreach (var pair in customTags)
            {
                if (propertyName.Equals(pair.Tag, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
        }

        if (propertyName.Equals("DATE", StringComparison.OrdinalIgnoreCase))
        {
            return DateTime.Today.ToShortDateString();
        }

        if (propertyName.Equals("TIME", StringComparison.OrdinalIgnoreCase))
        {
            return DateTime.Now.ToShortTimeString();
        }

        if (propertyName.StartsWith("ENV:", StringComparison.OrdinalIgnoreCase))
        {
            var key = propertyName.Substring(4);
            return Environment.GetEnvironmentVariable(key);
        }

        if (propertyName.StartsWith("DATE:", StringComparison.OrdinalIgnoreCase))
        {
            var format = propertyName.Substring(5);
            try
            {
                return DateTime.Now.ToString(format, CultureInfo.CurrentCulture);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public static string Format(string formatstring, params object[] formatitems)
    {
        try
        {
            return string.Format(Parse(formatstring), formatitems);
        }
        catch (FormatException ex)
        {
            LoggingService.Warn(ex);
            return Parse(formatstring);
        }
    }

    public static void RegisterStringTagProvider(IStringTagProvider tagProvider)
    {
    }

    public static void RegisterStringTagProvider(string prefix, IStringTagProvider tagProvider)
    {
    }
}

public readonly struct StringTagPair
{
    public string Tag { get; }

    public string Value { get; }

    public StringTagPair(string tag, string value)
    {
        Tag = tag ?? throw new ArgumentNullException(nameof(tag));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }
}
