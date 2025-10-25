using System.Windows;
using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using HslCommunication.LogNet;

namespace LaserCuttingDetector.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : ThemedWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            Messenger.Default.Register<HslMessageItem>(this, "Log", msg =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (msg == null) return;
                    LogBox.Items.Add(msg);
                    if (LogBox.Items.Count > 1000)
                    {
                        LogBox.Items.RemoveAt(0);
                    }
                    LogBox.ScrollIntoView(msg);
                });
            });
        }
    }
}
