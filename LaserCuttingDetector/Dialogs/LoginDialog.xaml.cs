using System.Collections.Generic;
using System.Windows;
using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using LaserCuttingDetector.Models;

namespace LaserCuttingDetector.Dialogs
{
    /// <summary>
    /// Interaction logic for LoginDialog.xaml
    /// </summary>
    public partial class LoginDialog : ThemedWindow
    { 
        public LoginDialog()
        {
            InitializeComponent();
            AccountSelector.ItemsSource = new List<string> {"工程师","管理员" };
            AccountSelector.SelectedIndex = 0;
        }

        public string Password;

        private void OkButton_OnClick(object sender, RoutedEventArgs e)
        {
            //合理性检查
            if (PasswordText.Text!=(AccountSelector.SelectedIndex==0?ConfigManager.Instance.Config.EngineerPassword:ConfigManager.Instance.Config.AdminPassword))
            {
                Messenger.Default.Send("密码错误",null,"MessageBox");
                return;
            }
            Password = PasswordText.Text;
            if (Password == ConfigManager.Instance.Config.EngineerPassword)
            {
                ConfigManager.Instance.CurrentPermissionLevel = PermissionLevel.Engineer;
            }
            else if (Password == ConfigManager.Instance.Config.AdminPassword)
            {
                ConfigManager.Instance.CurrentPermissionLevel = PermissionLevel.Admin;
            }
            DialogResult = true;
            Close();
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
