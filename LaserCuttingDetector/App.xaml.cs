using System.Windows;
using DevExpress.Mvvm;
using DevExpress.Xpf.Core;

namespace LaserCuttingDetector
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static App()
        {
            CompatibilitySettings.UseLightweightThemes = true;
            ApplicationThemeHelper.Preload(PreloadCategories.Core);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            HslCommunication.Authorization.SetAuthorizationCode("c04165f1-2578-4d86-ab08-a293b7552a5f");
            Models.LogManager.Instance.Init();
            SplashScreenManager.CreateThemed(new DXSplashScreenViewModel
                {
                    Copyright = "广东大族粤铭激光集团股份有限公司",
                    IsIndeterminate = true,

                    Status = "程序加载中，请稍等...",
                    Title = "木板视觉检测系统",
                    Subtitle = "您身边的智能制造专家"
                }
            ).ShowOnStartup();
            base.OnStartup(e);
        }
    }
}