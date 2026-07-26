using ICSharpCode.Core;

public class ResourceUsage
{
    public static void Use()
    {
        var core = ResourceService.GetString("SomeCoreKey");
        var bcl = SomeResourceManager.GetString("Greeting");
    }
}
