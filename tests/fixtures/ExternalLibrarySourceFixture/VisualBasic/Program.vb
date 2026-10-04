Imports Navlyn.ExternalFixture

Module Program
    Sub Main()
        Dim probe As New Probe()
        Dim selected As String = probe.Pick(7)
        Dim explicitConstructed = New Probe(7)
    End Sub
End Module
