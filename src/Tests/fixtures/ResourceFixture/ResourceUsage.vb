Imports ICSharpCode.Core

Public Class ResourceUsageVB
    Public Shared Sub Use()
        Dim core = ResourceService.GetString("SomeCoreKey")
        Dim bcl = SomeResourceManager.GetString("Greeting")
    End Sub
End Class
