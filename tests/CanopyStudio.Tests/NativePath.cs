namespace CanopyStudio.Tests;

// Tests spell game paths the Windows way, mods\Foo\canopy.ini. On Linux a
// backslash is a plain file name character, so Path.Combine would make one
// file named "mods\Foo\canopy.ini" instead of two folders and a file.
internal static class NativePath
{
    public static string Of(string path) => path.Replace('\\', Path.DirectorySeparatorChar);
}
