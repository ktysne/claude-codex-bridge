namespace CodexBridgeConsole
{
    internal static class Program
    {
        // Windows Forms のダイアログとクリップボードは単一スレッドアパートメントを前提とする。
        [System.STAThread]
        private static void Main()
        {
            // 段階 3 で MainForm を起動する形に差し替える。
        }
    }
}
