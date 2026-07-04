using NUnit.Framework;
using ICSharpCode.Core;
using System.IO;

namespace UnoDevelop.Core.Tests
{
    [TestFixture]
    public class PropertiesTests
    {
        [Test]
        public void SetAndGet_RoundTrip_ReturnsValue()
        {
            var p = new Properties();
            p.Set("key1", "value1");
            Assert.That(p.Get<string>("key1", "default"), Is.EqualTo("value1"));
        }

        [Test]
        public void Get_DefaultValue_WhenMissing()
        {
            var p = new Properties();
            Assert.That(p.Get("missing", "default"), Is.EqualTo("default"));
        }

        [Test]
        public void SetAndGet_Int_ReturnsValue()
        {
            var p = new Properties();
            p.Set("num", 42);
            Assert.That(p.Get("num", 0), Is.EqualTo(42));
        }

        [Test]
        public void Clone_ReturnsIndependentCopy()
        {
            var p = new Properties();
            p.Set("a", "1");
            var clone = (Properties)p.Clone();
            clone.Set("a", "2");
            Assert.That(p.Get<string>("a", ""), Is.EqualTo("1"));
            Assert.That(clone.Get<string>("a", ""), Is.EqualTo("2"));
        }

        [Test]
        public void SaveAndLoad_RoundTrip_RetainsValues()
        {
            var path = System.IO.Path.GetTempFileName();
            try
            {
                var fn = FileName.Create(path);
                var p = new Properties();
                p.Set("str", "hello");
                p.Save(fn!);

                var p2 = Properties.Load(fn!);
                Assert.That(p2.Get<string>("str", ""), Is.EqualTo("hello"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Contains_ReturnsTrue_WhenKeyExists()
        {
            var p = new Properties();
            p.Set("key", "val");
            Assert.That(p.Contains("key"), Is.True);
        }

        [Test]
        public void Contains_ReturnsFalse_WhenKeyMissing()
        {
            var p = new Properties();
            Assert.That(p.Contains("nonexistent"), Is.False);
        }
    }
}
