using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnoDevelop.Services;

namespace UnoDevelop.Core.Tests
{
    [TestFixture]
    public class BookmarkPersistenceTests
    {
        [Test]
        public void SerializeThenDeserialize_RoundtripsEntries()
        {
            var path = Path.GetTempFileName();
            try
            {
                var original = new List<UnoBookmarkManager.BreakpointEntry>
                {
                    new("/home/user/proj/src/foo.cs", 10),
                    new("/home/user/proj/src/bar.cs", 42),
                    new("/home/user/proj/src/baz.cs", 7)
                };

                UnoBookmarkManager.SerializeEntriesTo(path, original);
                Assert.That(File.Exists(path), Is.True);

                var loaded = UnoBookmarkManager.DeserializeEntriesFrom(path);
                Assert.That(loaded.Count, Is.EqualTo(3));
                Assert.That(loaded[0].FilePath, Is.EqualTo("/home/user/proj/src/foo.cs"));
                Assert.That(loaded[0].Line, Is.EqualTo(10));
                Assert.That(loaded[1].FilePath, Is.EqualTo("/home/user/proj/src/bar.cs"));
                Assert.That(loaded[1].Line, Is.EqualTo(42));
                Assert.That(loaded[2].FilePath, Is.EqualTo("/home/user/proj/src/baz.cs"));
                Assert.That(loaded[2].Line, Is.EqualTo(7));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void SerializeThenDeserialize_EmptyList()
        {
            var path = Path.GetTempFileName();
            try
            {
                var original = new List<UnoBookmarkManager.BreakpointEntry>();

                UnoBookmarkManager.SerializeEntriesTo(path, original);
                Assert.That(File.Exists(path), Is.True);

                var loaded = UnoBookmarkManager.DeserializeEntriesFrom(path);
                Assert.That(loaded.Count, Is.EqualTo(0));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void DeserializeFromNonexistentFile_ReturnsEmptyList()
        {
            var path = "/tmp/nonexistent-breakpoints-test.json";
            var loaded = UnoBookmarkManager.DeserializeEntriesFrom(path);
            Assert.That(loaded.Count, Is.EqualTo(0));
        }

        [Test]
        public void BookmarkPersistence_ToProjectAndBack_RoundtripsViaTempProject()
        {
            var path = Path.GetTempFileName();
            try
            {
                var original = new List<UnoBookmarkManager.BreakpointEntry>
                {
                    new("/home/user/test.cs", 15)
                };

                UnoBookmarkManager.SerializeEntriesTo(path, original);
                var loaded = UnoBookmarkManager.DeserializeEntriesFrom(path);

                Assert.That(loaded.Count, Is.EqualTo(1));
                Assert.That(loaded[0].FilePath, Is.EqualTo("/home/user/test.cs"));
                Assert.That(loaded[0].Line, Is.EqualTo(15));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void MultipleProjects_DontCorruptEachOther()
        {
            var pathA = Path.GetTempFileName();
            var pathB = Path.GetTempFileName();
            try
            {
                var entriesA = new List<UnoBookmarkManager.BreakpointEntry>
                {
                    new("/projA/a.cs", 1)
                };
                var entriesB = new List<UnoBookmarkManager.BreakpointEntry>
                {
                    new("/projB/b.cs", 99)
                };

                UnoBookmarkManager.SerializeEntriesTo(pathA, entriesA);
                UnoBookmarkManager.SerializeEntriesTo(pathB, entriesB);

                var loadedA = UnoBookmarkManager.DeserializeEntriesFrom(pathA);
                var loadedB = UnoBookmarkManager.DeserializeEntriesFrom(pathB);

                Assert.That(loadedA.Count, Is.EqualTo(1));
                Assert.That(loadedA[0].FilePath, Is.EqualTo("/projA/a.cs"));

                Assert.That(loadedB.Count, Is.EqualTo(1));
                Assert.That(loadedB[0].FilePath, Is.EqualTo("/projB/b.cs"));
            }
            finally
            {
                if (File.Exists(pathA)) File.Delete(pathA);
                if (File.Exists(pathB)) File.Delete(pathB);
            }
        }
    }
}
