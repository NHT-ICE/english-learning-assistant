using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

sealed class ImportSystemUia : ITypeLibImporterNotifySink {
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void LoadTypeLibEx(string path, int registration, out ITypeLib library);
    public void ReportEvent(ImporterEventKind kind, int code, string message) { if (kind != ImporterEventKind.NOTIF_TYPECONVERTED) Console.WriteLine(message); }
    public Assembly ResolveRef(object library) { throw new NotSupportedException("Unexpected referenced type library"); }
    static void Main(string[] args) {
        ITypeLib library;
        LoadTypeLibEx(System.IO.Path.Combine(Environment.SystemDirectory, "UIAutomationCore.dll"), 2, out library);
        var imported = new TypeLibConverter().ConvertTypeLibToAssembly(library, args[0], TypeLibImporterFlags.None, new ImportSystemUia(), null, null, "LearningUia", null);
        imported.Save(System.IO.Path.GetFileName(args[0]));
        Marshal.ReleaseComObject(library);
        Console.WriteLine("System UI Automation bindings generated.");
    }
}
