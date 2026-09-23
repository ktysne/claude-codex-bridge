using System.Windows.Forms;

namespace CodexBridgeConsole
{
    // 入力可(DropDown)のネイティブ ComboBox は、大きさが変わると編集欄の文字を全選択する。
    // 幅を中身に合わせて変えるたびに、フォーカスの無い欄まで選択表示になり、入力中の欄は次の打鍵で文字が置き換わるため、
    // 大きさの変更の前後で選択範囲を保つ。フォーカスの無い欄は選択を外す。
    internal sealed class SelectionPreservingComboBox : ComboBox
    {
        private const int WM_SIZE = 0x0005;
        private const int WM_WINDOWPOSCHANGED = 0x0047;

        protected override void WndProc(ref Message m)
        {
            bool resizing = m.Msg == WM_SIZE || m.Msg == WM_WINDOWPOSCHANGED;
            if (!resizing || DropDownStyle == ComboBoxStyle.DropDownList || !IsHandleCreated)
            {
                base.WndProc(ref m);
                return;
            }

            bool focused = Focused;
            int selectionStart = SelectionStart;
            int selectionLength = SelectionLength;
            base.WndProc(ref m);

            if (focused)
            {
                Select(selectionStart, selectionLength);
            }
            else if (SelectionLength != 0)
            {
                Select(0, 0);
            }
        }
    }
}
