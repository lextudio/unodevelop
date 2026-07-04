namespace ICSharpCode.Core.Migration;

public static class CoreMigrationBatch
{
    public static readonly string[] LinkedFiles =
    {
        "CoreException.cs",
        "Services/SDServiceAttribute.cs",
        "Services/ServiceNotFoundException.cs",
        "Services/ServiceSingleton.cs",
        "Services/LoggingService/ILoggingService.cs",
        "Services/LoggingService/LoggingService.cs",
        "Services/LoggingService/TextWriterLoggingService.cs",
        "Services/PropertyService/IPropertyService.cs",
        "Services/PropertyService/PropertyService.cs",
        "Services/PropertyService/PropertyServiceImpl.cs",
        "Services/PropertyService/Properties.uno.cs",
        "Services/MessageService/IMessageService.cs",
        "Services/MessageService/MessageService.cs",
        "Services/MessageService/TextWriterMessageService.cs",
        "Services/ResourceService/IResourceService.cs",
        "Services/ResourceService/ResourceNotFoundException.cs",
        "Services/StringParser/IStringTagProvider.cs",
        "Services/StringParser/PropertyObjectTagProvider.cs",
        "Services/StringParser/StringParser.uno.cs",
        "Services/FileUtility/PathName.cs",
        "Services/FileUtility/DirectoryName.cs",
        "Services/FileUtility/FileName.cs",
        "Services/FileUtility/FileUtility.Minimal.cs",
        "Services/FileUtility/FileUtility.uno.cs",
        "Util/CallbackOnDispose.cs",
        "Util/TraceTextWriter.cs",
        "ExtensionMethods.cs",
    };
}
