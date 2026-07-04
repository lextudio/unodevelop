using NUnit.Framework;
using ICSharpCode.Core;
using System.Collections.Generic;
using System.Linq;

namespace UnoDevelop.Core.Tests
{
    [TestFixture]
    public class TopologicalSortTests
    {
        [Test]
        public void Sort_SingleItem_ReturnsSame()
        {
            var c = CreateCodon("A");
            var input = new[] { new[] { c } };
            var sorted = TopologicalSort.Sort(input);
            Assert.That(sorted.Select(x => x.Id), Is.EqualTo(new[] { "A" }));
        }

        private static Codon CreateCodon(string id)
        {
            var properties = new Properties();
            properties.Set("id", id);
            return new Codon(null, id, properties, null);
        }
    }
}
