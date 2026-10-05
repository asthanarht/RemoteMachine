$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath
    if ($LASTEXITCODE -ne 0) { throw 'RDP type-library import failed.' }
    exit
}
$root = Split-Path $PSScriptRoot -Parent
$outputDirectory = Join-Path $root 'lib'
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$destination = Join-Path $outputDirectory 'Interop.MSTSCLib.dll'
Add-Type -TypeDefinition @'
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public sealed class RdpImportNotifications : ITypeLibImporterNotifySink
{
    public void ReportEvent(ImporterEventKind kind, int code, string message)
    {
        Console.WriteLine(kind + ": " + message);
        if (kind == ImporterEventKind.ERROR_REFTOINVALIDTYPELIB) throw new InvalidOperationException(message);
    }
    public Assembly ResolveRef(object typeLibrary)
    {
        throw new NotSupportedException("An unexpected referenced type library was requested.");
    }
}
public static class RdpTypeLibrary
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLibEx(string file, int kind, out ITypeLib library);
    public static void Import(string source, string destination)
    {
        ITypeLib library;
        LoadTypeLibEx(source, 2, out library);
        try
        {
            var converter = new TypeLibConverter();
            var assembly = converter.ConvertTypeLibToAssembly(library, destination,
                TypeLibImporterFlags.None, new RdpImportNotifications(), null, null, "MSTSCLib", null);
            assembly.Save(System.IO.Path.GetFileName(destination));
        }
        finally { Marshal.ReleaseComObject(library); }
    }
}
'@
Push-Location $outputDirectory
try {
    [RdpTypeLibrary]::Import("$env:WINDIR\System32\mstscax.dll", $destination)
    Write-Output "Generated $destination from the installed Microsoft RDP component."
} finally { Pop-Location }
