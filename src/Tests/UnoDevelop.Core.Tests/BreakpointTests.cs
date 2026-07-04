using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace UnoDevelop.Core.Tests
{
    [TestFixture]
    public class BreakpointTests
    {
        [Test]
        public void ToggleBreakpoint_AddsOnFirstCall()
        {
            var lines = new HashSet<int>();
            var result = Toggle(lines, 10);
            Assert.That(result, Is.True);
            Assert.That(lines, Does.Contain(10));
        }

        [Test]
        public void ToggleBreakpoint_RemovesOnSecondCall()
        {
            var lines = new HashSet<int> { 10 };
            var result = Toggle(lines, 10);
            Assert.That(result, Is.False);
            Assert.That(lines, Does.Not.Contain(10));
        }

        [Test]
        public void ToggleBreakpoint_MultipleLines()
        {
            var lines = new HashSet<int>();
            Toggle(lines, 5);
            Toggle(lines, 10);
            Toggle(lines, 15);
            Assert.That(lines.Count, Is.EqualTo(3));
            Assert.That(lines, Does.Contain(5));
            Assert.That(lines, Does.Contain(10));
            Assert.That(lines, Does.Contain(15));
        }

        [Test]
        public void SetBreakpoints_ReplacesExisting()
        {
            var lines = new HashSet<int> { 1, 2, 3 };
            lines.Clear();
            lines.UnionWith(new[] { 10, 20 });
            Assert.That(lines.Count, Is.EqualTo(2));
            Assert.That(lines, Does.Contain(10));
            Assert.That(lines, Does.Contain(20));
        }

        [Test]
        public void BreakpointLines_ReturnedSorted()
        {
            var lines = new List<int>();
            var bpSet = new HashSet<int>();
            int lastNotified = -1;

            void Notify(IReadOnlyList<int> sorted)
            {
                lines = sorted.ToList();
                lastNotified = sorted.Count;
            }

            ToggleWithNotify(bpSet, 15, Notify);
            ToggleWithNotify(bpSet, 3, Notify);
            ToggleWithNotify(bpSet, 8, Notify);

            Assert.That(lastNotified, Is.EqualTo(3));
            Assert.That(lines[0], Is.EqualTo(3));
            Assert.That(lines[1], Is.EqualTo(8));
            Assert.That(lines[2], Is.EqualTo(15));
        }

        [Test]
        public void SetBreakpoints_ReplaceSet()
        {
            var bpSet = new HashSet<int> { 1, 2, 3 };
            Assert.That(bpSet.Count, Is.EqualTo(3));

            bpSet.Clear();
            bpSet.UnionWith(new[] { 10, 20 });
            Assert.That(bpSet.Count, Is.EqualTo(2));
            Assert.That(bpSet, Does.Contain(10));
            Assert.That(bpSet, Does.Contain(20));
        }

        private static bool Toggle(HashSet<int> set, int line)
        {
            if (set.Contains(line))
            {
                set.Remove(line);
                return false;
            }
            else
            {
                set.Add(line);
                return true;
            }
        }

        private static void ToggleWithNotify(HashSet<int> set, int line, System.Action<IReadOnlyList<int>> notify)
        {
            Toggle(set, line);
            notify(set.OrderBy(l => l).ToList());
        }
    }
}
