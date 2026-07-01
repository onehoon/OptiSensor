using System.Runtime.InteropServices;
using System.Text;

namespace OptiSensor;

internal static class ConsoleBridge
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    public static void AttachForCliMode()
    {
        if (!AttachConsole(AttachParentProcess))
            AllocConsole();

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch
        {
        }
    }
}
