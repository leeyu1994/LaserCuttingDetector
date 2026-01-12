using System;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication.ModBus;

namespace LaserCuttingDetector.Models
{
    /// <summary>
    /// 基于 HslCommunication 的 Modbus RTU over TCP 通讯封装。
    /// </summary>
    public class PlcService : IDisposable
    {
        private readonly ModbusRtuOverTcp _client;
        private CancellationTokenSource? _heartbeatCts;
        private bool _heartbeatFlag;

        public PlcService(string ipAddress, int port, byte station)
        {
            _client = new ModbusRtuOverTcp(ipAddress, port)
            {
                Station = station
            };
        }

        public bool IsConnected { get; private set; }

        public async Task<bool> ConnectAsync()
        {
            var result = await Task.Run(() => _client.ConnectServer());
            IsConnected = result.IsSuccess;
            return IsConnected;
        }

        public void Disconnect()
        {
            StopHeartbeat();
            if (IsConnected)
            {
                _client.ConnectClose();
                IsConnected = false;
            }
        }

        public void StartHeartbeat(TimeSpan? interval = null)
        {
            StopHeartbeat();
            _heartbeatCts = new CancellationTokenSource();
            var token = _heartbeatCts.Token;
            var delay = interval ?? TimeSpan.FromSeconds(1);

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    _heartbeatFlag = !_heartbeatFlag;
                    await WriteAsync("D400", _heartbeatFlag ? 1 : 0);
                    try
                    {
                        await Task.Delay(delay, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }

        public void StopHeartbeat()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;
        }

        public async Task<int?> ReadScanModeAsync()
        {
            var result = await Task.Run(() => _client.ReadInt32("D401"));
            return result.IsSuccess ? result.Content : null;
        }

        public async Task<int?> ReadOnlineModeAsync()
        {
            var result = await Task.Run(() => _client.ReadInt32("D450"));
            return result.IsSuccess ? result.Content : null;
        }

        public async Task ReportDetectionAsync(int currentStatus, int previousStatus)
        {
            await WriteAsync("D403", currentStatus);
            await WriteAsync("D404", previousStatus);
            await WriteAsync("D402", 1);
        }

        public async Task AcknowledgeScanModeSwitchAsync(int scanMode)
        {
            await WriteAsync("D401", scanMode);
        }

        private async Task WriteAsync(string address, int value)
        {
            await Task.Run(() => _client.Write(address, value));
        }

        public void Dispose()
        {
            StopHeartbeat();
            _client.Dispose();
        }
    }
}
