using NUnit.Framework;
using ICSharpCode.Core;

namespace UnoDevelop.Core.Tests
{
    [TestFixture]
    public class StringParserTests
    {
        [Test]
        public void Parse_PlainText_ReturnsSame()
        {
            var result = StringParser.Parse("hello world");
            Assert.That(result, Is.EqualTo("hello world"));
        }

        [Test]
        public void Parse_NullInput_ReturnsNull()
        {
            Assert.That(StringParser.Parse(null!), Is.Null);
        }

        [Test]
        public void Parse_WithCustomTag_ReplacesTag()
        {
            var result = StringParser.Parse("${key}", new StringTagPair("key", "value"));
            Assert.That(result, Is.EqualTo("value"));
        }
    }
}
