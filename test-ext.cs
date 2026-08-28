using System;
using System.IO;

public class Program {
    public static void Main() {
        Console.WriteLine(Path.ChangeExtension("NATIVES/001/report.eml.eml", ".tif"));
        Console.WriteLine(Path.ChangeExtension("NATIVES\\001\\report.eml.eml", ".tif"));
    }
}
