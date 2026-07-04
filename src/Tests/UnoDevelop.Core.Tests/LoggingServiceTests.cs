using NUnit.Framework;
using ICSharpCode.Core.Implementation;
using System.IO;

namespace UnoDevelop.Core.Tests
{
    [TestFixture]
    public class LoggingServiceTests
    {
        [Test]
        public void TextWriterLoggingService_WritesToWriter()
        {
            using var sw = new StringWriter();
            var service = new TextWriterLoggingService(sw);
            service.Info("test info");
            Assert.That(sw.ToString(), Does.Contain("test info"));
        }

        [Test]
        public void TextWriterLoggingService_Warn_WritesWarning()
        {
            using var sw = new StringWriter();
            var service = new TextWriterLoggingService(sw);
            service.Warn("warning message");
            Assert.That(sw.ToString(), Does.Contain("warning message"));
        }

        [Test]
        public void TextWriterLoggingService_Debug_WritesDebug()
        {
            using var sw = new StringWriter();
            var service = new TextWriterLoggingService(sw);
            service.Debug("debug info");
            Assert.That(sw.ToString(), Does.Contain("debug info"));
        }
    }
}
