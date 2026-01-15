using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace WSLServiceAgent
{
    static class Assets
    {
        private static Icon? _runningIcon;
        private static Icon? _stoppedIcon;
        private static Icon? _startIcon;
        private static Icon? _stopIcon;
        private static Icon? _terminalIcon;
        private static Icon? _exitIcon;
        private static Icon? _appIcon;

        private static Bitmap? _runningBitmap;
        private static Bitmap? _stoppedBitmap;
        private static Bitmap? _startBitmap;
        private static Bitmap? _stopBitmap;
        private static Bitmap? _terminalBitmap;
        private static Bitmap? _exitBitmap;

        public static Icon RunningIcon => _runningIcon ??= LoadIcon("running.png");
        public static Icon StoppedIcon => _stoppedIcon ??= LoadIcon("stopped.png");
        public static Icon StartIcon => _startIcon ??= LoadIcon("start.png");
        public static Icon StopIcon => _stopIcon ??= LoadIcon("stop.png");
        public static Icon TerminalIcon => _terminalIcon ??= LoadIcon("terminal.png");
        public static Icon ExitIcon => _exitIcon ??= LoadIcon("exit.png");
        public static Icon AppIcon => _appIcon ??= LoadIcon("app.ico");

        public static Bitmap RunningBitmap => _runningBitmap ??= LoadBitmap("running.png");
        public static Bitmap StoppedBitmap => _stoppedBitmap ??= LoadBitmap("stopped.png");
        public static Bitmap StartBitmap => _startBitmap ??= LoadBitmap("start.png");
        public static Bitmap StopBitmap => _stopBitmap ??= LoadBitmap("stop.png");
        public static Bitmap TerminalBitmap => _terminalBitmap ??= LoadBitmap("terminal.png");
        public static Bitmap ExitBitmap => _exitBitmap ??= LoadBitmap("exit.png");

        private static Bitmap LoadBitmap(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fullResourceName = $"WSLServiceAgent.Resources.{resourceName}";

                var stream = assembly.GetManifestResourceStream(fullResourceName);
                if (stream == null)
                {
                    return new Bitmap(16, 16);
                }

                return new Bitmap(stream);
            }
            catch
            {
                return new Bitmap(16, 16);
            }
        }

        private static Icon LoadIcon(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fullResourceName = $"WSLServiceAgent.Resources.{resourceName}";

                using var stream = assembly.GetManifestResourceStream(fullResourceName);
                if (stream == null)
                {
                    // Resource not found, use system icon
                    return SystemIcons.Application;
                }

                if (resourceName.EndsWith(".ico"))
                {
                    return new Icon(stream);
                }
                else if (resourceName.EndsWith(".png"))
                {
                    using var bitmap = new Bitmap(stream);
                    IntPtr hIcon = bitmap.GetHicon();
                    return Icon.FromHandle(hIcon);
                }

                return SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }
    }
}
