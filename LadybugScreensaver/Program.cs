using LadybugScreensaver;

// Windows passes one of three arguments when invoking a screensaver:
//   /s — run fullscreen (normal screensaver activation)
//   /p <handle> — render into the preview window in Screen Saver Settings
//   /c — show a settings dialog (we don't implement one)
// If no argument is passed (e.g. running directly), default to fullscreen.

ApplicationConfiguration.Initialize();
Application.SetCompatibleTextRenderingDefault(false);

string firstArg = args.Length > 0 ? args[0].ToLower().Trim() : "/s";

if (firstArg.StartsWith("/p"))
{
    // Windows passes the preview window handle either as "/p12345" or "/p 12345"
    string handleStr = firstArg.Length > 2
        ? firstArg.Substring(2)
        : args.Length > 1 ? args[1] : "";

    if (long.TryParse(handleStr, out long handleValue))
    {
        IntPtr previewHandle = new IntPtr(handleValue);
        Application.Run(new ScreensaverForm(previewHandle));
    }
}
else if (firstArg.StartsWith("/c"))
{
    // No settings dialog implemented — do nothing
}
else
{
    Application.Run(new ScreensaverForm());
}
