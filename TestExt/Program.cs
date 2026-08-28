using System;
using System.IO;

class Program {
    static void Main() {
        Console.WriteLine(Path.ChangeExtension("NATIVES/001/report.eml.eml", ".tif"));
        Console.WriteLine(Path.ChangeExtension("NATIVES\\v1.0\\report.eml.eml", ".tif"));
    }
}
