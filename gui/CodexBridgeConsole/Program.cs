namespace CodexBridgeConsole
{
    internal static class Program
    {
        // Windows Forms のダイアログとクリップボードは単一スレッドアパートメントを前提とする。
        [System.STAThread]
        private static void Main()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.Run(new MainForm());
        }
    }
}
