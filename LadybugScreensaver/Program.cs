using LadybugScreensaver;

ApplicationConfiguration.Initialize();
Application.SetCompatibleTextRenderingDefault(false);

string firstArg = args.Length > 0 ? args[0].ToLower().Trim() : "/s";

if (firstArg.StartsWith("/p") && args.Length > 1)
{
    IntPtr previewHandle = new IntPtr(long.Parse(args[1]));
    Application.Run(new ScreensaverForm(previewHandle));
}
else if (firstArg.StartsWith("/c"))
{
    // No settings dialog — just do nothing
}
else
{
    Application.Run(new ScreensaverForm());
}