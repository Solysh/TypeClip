namespace TypeClip;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Only one instance: a second launch exits immediately.
        using var mutex = new Mutex(true, @"Local\TypeClip.SingleInstance", out bool created);
        if (!created) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
