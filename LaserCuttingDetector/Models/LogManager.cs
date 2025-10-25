using System;
using DevExpress.Mvvm;
using HslCommunication.LogNet;

namespace LaserCuttingDetector.Models
{
    public class LogManager
    {
        // ReSharper disable once InconsistentNaming
        private static readonly Lazy<LogManager> _instance = new Lazy<LogManager>(() => new LogManager());

        public static LogManager Instance => _instance.Value;

        private LogManager()
        {
        }

        private ILogNet _logNet;
        private ILogNet _customerLogNet;
        public void Init()
        {
            _logNet = new LogNetDateTime("Log", GenerateMode.ByEveryDay);
            _customerLogNet = new LogNetDateTime("Log\\Customer", GenerateMode.ByEveryDay);
            _logNet.BeforeSaveToFile += _logNet_BeforeSaveToFile;
        }

        private void _logNet_BeforeSaveToFile(object sender, HslEventArgs e)
        {
            Messenger.Default.Send(e.HslMessage, "Log");
        }

        public void Info(string msg, bool customer = false)
        {
            _logNet?.WriteInfo(msg);
            if (customer)
            {
                _customerLogNet?.WriteInfo(msg);
            }
        }

        public void Error(string msg, bool customer = false)
        {
            _logNet?.WriteError(msg);
            if (customer)
            {
                _customerLogNet?.WriteInfo(msg);
            }
        }

        public void Debug(string msg, bool customer = false)
        {
            _logNet?.WriteDebug(msg);
            if (customer)
            {
                _customerLogNet?.WriteInfo(msg);
            }
        }

        public void Fatal(string msg, bool customer = false)
        {
            _logNet?.WriteFatal(msg);
            if (customer)
            {
                _customerLogNet?.WriteInfo(msg);
            }
        }

        public void Warn(string msg, bool customer = false)
        {
            _logNet?.WriteWarn(msg);
            if (customer)
            {
                _customerLogNet?.WriteInfo(msg);
            }
        }
    }

}
