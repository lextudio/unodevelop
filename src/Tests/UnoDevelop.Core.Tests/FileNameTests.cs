using NUnit.Framework;
using ICSharpCode.Core;

namespace UnoDevelop.Core.Tests
{
    [TestFixture]
    public class FileNameTests
    {
        [Test]
        public void Create_AbsolutePath_ReturnsFileName()
        {
            var fn = FileName.Create("/home/user/project.cs");
            Assert.That(fn, Is.Not.Null);
        }

        [Test]
        public void Create_Null_ReturnsNull()
        {
            Assert.That(FileName.Create(null!), Is.Null);
        }

        [Test]
        public void Equality_SamePath_AreEqual()
        {
            var a = FileName.Create("/path/to/file.cs");
            var b = FileName.Create("/path/to/file.cs");
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void GetFileName_ReturnsFileNameWithExtension()
        {
            var fn = FileName.Create("/dir/sub/file.txt");
            Assert.That(fn!.GetFileName(), Is.EqualTo("file.txt"));
        }

        [Test]
        public void HasExtension_WithExtension_ReturnsTrue()
        {
            var fn = FileName.Create("test.cs");
            Assert.That(fn!.HasExtension(".cs"), Is.True);
        }

        [Test]
        public void ImplicitConversion_ToString_ReturnsPath()
        {
            var fn = FileName.Create("/path/to/file.cs");
            string path = fn!;
            Assert.That(path, Is.EqualTo("//path/to/file.cs"));
        }
    }
}
