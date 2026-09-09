using System;
using System.IO;

namespace CodexBridgeConsole
{
    public sealed class FrontMatterFileChangedException : IOException
    {
        public FrontMatterFileChangedException(string path)
            : this(path, null)
        {
        }

        public FrontMatterFileChangedException(string path, Exception innerException)
            : base("読み込み後にファイルが変更または削除されたため保存できない: " + path, innerException)
        {
        }
    }
}
