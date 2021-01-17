using Microsoft.VisualStudio.Shell;
using System.ComponentModel;

namespace HotStatus
{
    public class HotStatusOptions : DialogPage
    {
        private static HotStatusOptions instance;
        public static HotStatusOptions Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new HotStatusOptions();
                }
                return instance;
            }
        }

        public HotStatusOptions()
        {
            instance = this;
        }

        private bool showErrorInfo = true;
        private bool showSymbolInfo = true;

        [Category("Hot Status General Settings")]
        [DisplayName("Show Error Info")]
        [Description("Show Error Info on status bar")]
        public bool ShowErrorInfo
        {
            get { return showErrorInfo; }
            set { showErrorInfo = value; }
        }

        [Category("Hot Status General Settings")]
        [DisplayName("Show Symbol Info")]
        [Description("Show Symbol Info on status bar")]
        public bool ShowSymbolInfo
        {
            get { return showSymbolInfo; }
            set { showSymbolInfo = value; }
        }
    }
}
