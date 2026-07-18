namespace StreamDecky.Helpers;

public static class ClipboardHelper
{
    public static bool TrySetText(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            return true;
        }
        catch
        {
            // Clipboard can fail transiently if another process has it locked.
            return false;
        }
    }
}
