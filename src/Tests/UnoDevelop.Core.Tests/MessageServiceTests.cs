using NUnit.Framework;
using ICSharpCode.Core.Implementation;
using System.IO;

namespace UnoDevelop.Core.Tests
{
    [TestFixture]
    public class MessageServiceTests
    {
        [Test]
        public void TextWriterMessageService_ShowMessage_WritesMessage()
        {
            using var sw = new StringWriter();
            var service = new TextWriterMessageService(sw);
            service.ShowMessage("hello", "Test");
            Assert.That(sw.ToString(), Does.Contain("hello"));
        }

        [Test]
        public void TextWriterMessageService_ShowWarning_WritesWarning()
        {
            using var sw = new StringWriter();
            var service = new TextWriterMessageService(sw);
            service.ShowWarning("warning");
            Assert.That(sw.ToString(), Does.Contain("warning"));
        }
    }
}
