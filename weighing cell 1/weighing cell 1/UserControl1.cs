using System;
using System.IO.Ports;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Text;
using System.Drawing;
using System.Timers;
using System.Windows.Forms.DataVisualization.Charting;

namespace WeighingSensorControl
{
    public partial class WeighingSensorControl : UserControl
    {
        // 硬件配置
        private SerialPort _serialPort;
        private System.Timers.Timer _samplingTimer;
        private double _currentWeight = 0;
        private bool _isConnected = false;
        private int _selectedBaudRate = 9600;
        private int _selectedSamplingInterval = 1000;
        private byte _selectedDeviceAddress = 0x12;


        // UI控件
        private ComboBox _comboPorts;
        private TextBox _txtWeight;
        private TextBox _txtLog;
        private Button _btnStartStop;
        private Button _btnClear;
        private Chart _chart;

        public WeighingSensorControl()
        {
            InitializeComponent();
            SetupUI();
            InitializeTimer();
        }

        #region UI初始化
        private void SetupUI()
        {
            this.Size = new Size(1000, 600);  // 扩大窗口尺寸
            this.BackColor = Color.White;

            // 端口选择框（左上角）
            _comboPorts = new ComboBox
            {
                Location = new Point(10, 30),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            RefreshPortList();
            this.Controls.Add(_comboPorts);
            this.Controls.Add(new Label { Text = "通讯端口", Location = new Point(10, _comboPorts.Top - 20) });

            // 波特率选择框（端口框右侧）
            ComboBox comboBaudRate = new ComboBox
            {
                Location = new Point(_comboPorts.Right + 10, 30),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            comboBaudRate.Items.AddRange(new object[] { 9600, 19200, 38400, 57600, 115200 });
            comboBaudRate.SelectedIndex = 0;
            comboBaudRate.SelectedIndexChanged += (s, e) =>
                _selectedBaudRate = int.Parse(comboBaudRate.SelectedItem.ToString());
            this.Controls.Add(comboBaudRate);
            this.Controls.Add(new Label { Text = "波特率", Location = new Point(comboBaudRate.Left, comboBaudRate.Top - 20) });

            // 采样周期选择框（波特率框右侧）
            ComboBox comboSamplingInterval = new ComboBox
            {
                Location = new Point(comboBaudRate.Right + 10, 30),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            comboSamplingInterval.Items.AddRange(new object[] { 500, 1000, 2000, 5000 });
            comboSamplingInterval.SelectedIndex = 1;
            comboSamplingInterval.SelectedIndexChanged += (s, e) =>
            {
                _selectedSamplingInterval = int.Parse(comboSamplingInterval.SelectedItem.ToString());
                if (_samplingTimer != null)
                    _samplingTimer.Interval = _selectedSamplingInterval;
            };
            this.Controls.Add(comboSamplingInterval);
            this.Controls.Add(new Label { Text = "采样周期(ms)", Location = new Point(comboSamplingInterval.Left, comboSamplingInterval.Top - 20) });

            // 设备地址选择框（采样周期框右侧）
            ComboBox comboDeviceAddress = new ComboBox
            {
                Location = new Point(comboSamplingInterval.Right + 10, 30),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            comboDeviceAddress.Items.AddRange(new object[] { "0x01", "0x02", "0x12" });
            comboDeviceAddress.SelectedIndex = 2;
            comboDeviceAddress.SelectedIndexChanged += (s, e) =>
                _selectedDeviceAddress = Convert.ToByte(comboDeviceAddress.SelectedItem.ToString(), 16);
            this.Controls.Add(comboDeviceAddress);
            this.Controls.Add(new Label { Text = "设备地址", Location = new Point(comboDeviceAddress.Left, comboDeviceAddress.Top - 20) });

            // 开始/停止按钮（第二行）
            _btnStartStop = new Button
            {
                Text = "开始采集",
                Location = new Point(10, 60),
                Size = new Size(80, 23)
            };
            _btnStartStop.Click += BtnStartStop_Click;
            this.Controls.Add(_btnStartStop);

            // 清空按钮（开始按钮右侧）
            _btnClear = new Button
            {
                Text = "清空数据",
                Location = new Point(_btnStartStop.Right + 10, 60),
                Size = new Size(80, 23)
            };
            _btnClear.Click += BtnClear_Click;
            this.Controls.Add(_btnClear);

            // 重量显示框（第三行）
            _txtWeight = new TextBox
            {
                Location = new Point(10, 160),
                Size = new Size(200, 50),
                Font = new Font("Microsoft YaHei", 24),
                TextAlign = HorizontalAlignment.Right,
                ReadOnly = true,
                Text = "0.000 吨"
            };
            this.Controls.Add(_txtWeight);

            // 日志框（右侧区域）
            _txtLog = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(250, 60),
                Size = new Size(520, 150),
                ReadOnly = true
            };
            this.Controls.Add(_txtLog);

            // 图表（底部区域）
            _chart = new Chart
            {
                Location = new Point(10, 250),
                Size = new Size(this.Width - 30, 280)
            };
            var chartArea = new ChartArea();
            _chart.ChartAreas.Add(chartArea);
            var series = new Series { ChartType = SeriesChartType.Line, Color = Color.Blue };
            _chart.Series.Add(series);
            this.Controls.Add(_chart);
        }
        #endregion

        #region 核心功能
        private void RefreshPortList()
        {
            _comboPorts.Items.Clear();
            _comboPorts.Items.AddRange(SerialPort.GetPortNames());
            if (_comboPorts.Items.Count > 0)
                _comboPorts.SelectedIndex = 0;
        }

        private void InitializeTimer()
        {
            _samplingTimer = new System.Timers.Timer(_selectedSamplingInterval);
            _samplingTimer.Elapsed += async (s, e) => await SamplingTimer_Tick();
            _samplingTimer.AutoReset = true;
        }
        private async void BtnStartStop_Click(object sender, EventArgs e)
        {
            _btnStartStop.Enabled = false;

            if (!_isConnected)
            {
                if (await InitializeConnection())
                {
                    _isConnected = true;
                    _btnStartStop.Text = "停止采集";
                    _samplingTimer.Start();
                    Log("采集已启动");
                }
            }
            else
            {
                _isConnected = false;
                _btnStartStop.Text = "开始采集";
                _samplingTimer.Stop();
                CloseConnection();
                Log("采集已停止");
            }

            _btnStartStop.Enabled = true;
        }

        private async Task<bool> InitializeConnection()
        {
            if (_comboPorts.SelectedItem == null)
            {
                Log("错误：未选择串口");
                return false;
            }

            try
            {
                _serialPort = new SerialPort(
                    _comboPorts.SelectedItem.ToString(),
                    _selectedBaudRate,  // 使用用户选择的波特率
                    Parity.None,
                    8,
                    StopBits.One)
                {
                    Handshake = Handshake.None,
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };

                _serialPort.Open();

                // 使用用户选择的设备地址（原固定值0x12）
                byte[] wakeCommand = { 0x11, 0x42, 0x3F, _selectedDeviceAddress, 0x0D };
                _serialPort.Write(wakeCommand, 0, wakeCommand.Length);
                await Task.Delay(200);

                Log($"已连接到 {_serialPort.PortName}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"连接失败: {ex.Message}");
                return false;
            }
        }

        private void CloseConnection()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                    Log("串口连接已关闭");
                }
            }
            catch (Exception ex)
            {
                Log($"关闭连接时出错: {ex.Message}");
            }
        }

        private async Task SamplingTimer_Tick()
        {
            var weight = await ReadWeight();
            if (weight.HasValue)
            {
                _currentWeight = weight.Value;
                UpdateUI();
            }
        }

        private async Task<double?> ReadWeight()
        {
            try
            {
                // 发送读取即时重量命令（ASCII命令B: 0x42）
                byte[] command = { 0x11, 0x42, 0x3F, _selectedDeviceAddress, 0x0D };
                _serialPort.Write(command, 0, command.Length);

                byte[] response = new byte[10];
                int bytesRead = 0;
                DateTime start = DateTime.Now;

                while (bytesRead < 10 && (DateTime.Now - start).TotalMilliseconds < 1000)
                {
                    if (_serialPort.BytesToRead > 0)
                        bytesRead += _serialPort.Read(response, bytesRead, 10 - bytesRead);
                    await Task.Delay(10);
                }

                // 打印原始响应帧
                Log($"响应帧: {BitConverter.ToString(response, 0, bytesRead)}");

                if (bytesRead != 10 || response[0] != 0x11 || response[1] != 0x42 || response[9] != 0x0D)
                {
                    Log("错误：响应帧结构不完整或无效");
                    return null;
                }

                // 计算校验和
                byte checksum = 0;
                for (int i = 0; i < 8; i++) checksum += response[i];
                checksum = (byte)(checksum & 0x7F);
                if (checksum == 0x0D) checksum++;

                if (checksum != response[8])
                {
                    Log($"校验和错误 (计算值: {checksum:X2}, 实际值: {response[8]:X2})");
                    return null;
                }

                // 解析重量数据（直接取二进制值）
                int x1 = (char)response[2] - '0'; // 0x30 → 0
                int x2 = (char)response[3] - '0'; // 0x30 → 0
                int x3 = (char)response[4] - '0'; // 0x30 → 0
                int x4 = (char)response[5] - '0'; // 0x30 → 0
                int x5 = (char)response[6] - '0'; // 0x30 → 0
                byte x6 = response[7];

                // 计算原始内码值（假设为十进制数字组合）
                double rawValue = x5 * 10000 + x4 * 1000 + x3 * 100 + x2 * 10 + x1;

                // 处理符号和小数位
                bool isNegative = (x6 & 0x04) != 0; // 符号位在 bit2
                int decimalPlaces = x6 & 0x03;      // 小数位在 bit0-1
                double weight = rawValue / Math.Pow(10, decimalPlaces);
                if (isNegative) weight = -weight;

                // 转换为吨（假设单位为公斤）
                double weightInTon = weight;

                Log($"实际重量: {weightInTon:F3} 吨");
                return weightInTon;
            }
            catch (Exception ex)
            {
                Log($"读取重量时出错: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region UI更新
        private void UpdateUI()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((Action)(() => UpdateUI()));
                return;
            }

            // 更新重量显示
            _txtWeight.Text = $"{_currentWeight:F3} 吨";

            // 更新图表
            if (_chart.Series.Count > 0)
            {
                _chart.Series[0].Points.AddY(_currentWeight);
                if (_chart.Series[0].Points.Count > 100)
                {
                    _chart.Series[0].Points.RemoveAt(0);
                }
            }
        }

        private void Log(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((Action)(() => Log(message)));
                return;
            }
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            _txtLog.ScrollToCaret();
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            _currentWeight = 0;
            _txtWeight.Text = "0.000 吨";
            if (_chart.Series.Count > 0)
            {
                _chart.Series[0].Points.Clear();
            }
            _txtLog.Clear();
        }
        #endregion


    }
}


