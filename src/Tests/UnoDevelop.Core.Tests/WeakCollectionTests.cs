using NUnit.Framework;
using ICSharpCode.Core;
using System.Linq;

namespace UnoDevelop.Core.Tests
{
    [TestFixture]
    public class WeakCollectionTests
    {
        [Test]
        public void Add_Item_CanEnumerate()
        {
            var coll = new WeakCollection<string>();
            coll.Add("one");
            coll.Add("two");
            var items = coll.ToList();
            Assert.That(items, Does.Contain("one"));
            Assert.That(items, Does.Contain("two"));
        }

        [Test]
        public void Remove_Item_NoLongerEnumerated()
        {
            var coll = new WeakCollection<string>();
            coll.Add("one");
            coll.Add("two");
            coll.Remove("one");
            var items = coll.ToList();
            Assert.That(items, Does.Contain("two"));
            Assert.That(items, Does.Not.Contain("one"));
        }

        [Test]
        public void Remove_MissingItem_NoException()
        {
            var coll = new WeakCollection<string>();
            coll.Add("a");
            Assert.DoesNotThrow(() => coll.Remove("nonexistent"));
        }
    }
}
