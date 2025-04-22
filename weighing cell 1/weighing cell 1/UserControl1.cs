using System;
using System.IO; 
using System.IO.Ports;
using System.Windows.Forms;
using System.Drawing;
using System.Linq;



namespace weighing_cell_1
{
    public partial class UserControl1 : UserControl
    {
        private SerialPort _serialPort;
        private Timer _samplingTimer;

        public UserControl1()
        {
            InitializeComponent();
            InitializeControls();
        }

        // 初始化控件内容
        private void InitializeControls()
        {
            // 填充COM端口
            comboBox1.Items.AddRange(new[] { "COM1", "COM2", "COM3", "COM4", "COM5" });
            comboBox1.SelectedIndex = 0;

            // 填充波特率
            comboBox2.Items.AddRange(new object[] { 2400, 4800, 9600, 19200 });
            comboBox2.SelectedIndex = 2; // 默认9600

            // 填充采样周期
            comboBox3.Items.AddRange(new object[] { 0.1, 0.5, 1.0, 5.0, 10.0 });
            comboBox3.SelectedIndex = 0; // 默认1s

            // 填充采集地址
            for (int i = 1; i <= 50; i++) comboBox4.Items.Add(i);
            comboBox4.SelectedIndex = 0;

            // 初始化定时器
            _samplingTimer = new Timer { Interval = 1000 }; // 默认1秒
            _samplingTimer.Tick += SamplingTimer_Tick;
        }


        // 点击“开始/停止采集”按钮
        private void button1_Click_1(object sender, EventArgs e)
        {
            try
            {
                if (button1.Text == "开始采集")
                {
                    // 检查串口是否已打开
                    if (_serialPort == null || !_serialPort.IsOpen)
                    {
                        // 获取可用端口时添加错误处理
                        string[] ports;
                        try
                        {
                            ports = SerialPort.GetPortNames();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"获取COM端口失败：{ex.Message}");
                            return;
                        }

                        if (ports.Length == 0)
                        {
                            MessageBox.Show("未检测到可用COM端口，请检查设备连接！");
                            return;
                        }

                        // 验证所选端口是否可用
                        string selectedPort = comboBox1.SelectedItem.ToString();
                        if (!ports.Contains(selectedPort))
                        {
                            MessageBox.Show($"选择的端口{selectedPort}不可用，请重新选择！");
                            return;
                        }

                        try
                        {
                            // 配置串口
                            _serialPort = new SerialPort(
                                selectedPort,
                                (int)comboBox2.SelectedItem,
                                Parity.None,
                                8,
                                StopBits.One)
                            {
                                ReadTimeout = 500,
                                WriteTimeout = 500  // 添加写入超时
                            };

                            _serialPort.Open();

                            // 发送测试命令并验证响应
                            byte[] testCommand = BuildCommandFrame(0x11, 0x44, 0x3F);
                            _serialPort.DiscardInBuffer(); // 清除输入缓冲区
                            _serialPort.Write(testCommand, 0, testCommand.Length);

                            // 改进的响应检测
                            int bytesRead = 0;
                            DateTime startTime = DateTime.Now;
                            while (bytesRead < 10 && (DateTime.Now - startTime).TotalMilliseconds < 1000)
                            {
                                if (_serialPort.BytesToRead > 0)
                                {
                                    bytesRead += _serialPort.BytesToRead;
                                }
                                System.Threading.Thread.Sleep(50);
                            }

                            if (bytesRead < 10)
                            {
                                throw new TimeoutException("传感器响应超时");
                            }
                        }
                        catch (TimeoutException)
                        {
                            MessageBox.Show("传感器无响应，请检查连接和参数设置！");
                            ResetConnection();
                            return;
                        }
                    }

                    // 启动采集
                    _samplingTimer.Interval = (int)((double)comboBox3.SelectedItem * 1000);
                    _samplingTimer.Start();
                    button1.Text = "停止采集";
                    textBox1.BackColor = Color.LightGreen;
                }
                else
                {
                    // 停止采集
                    ResetConnection();
                }
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("COM端口被其他程序占用或没有访问权限！");
                ResetConnection();
            }
            catch (IOException ex)
            {
                MessageBox.Show($"串口通信错误：{ex.Message}");
                ResetConnection();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败：{ex.Message}\n\n详细错误：{ex.ToString()}");
                ResetConnection();
            }
        }

        // 重置连接状态
        private void ResetConnection()
        {
            _samplingTimer?.Stop();
            _serialPort?.Close();
            button1.Text = "开始采集";
            textBox1.BackColor = Color.White;
        }

        // 定时采集任务
        // 定时采集任务（带超时检测）
        private DateTime _lastResponseTime = DateTime.MinValue;
        private void SamplingTimer_Tick(object sender, EventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            try
            {
                // 发送读取命令
                byte address = (byte)((int)comboBox4.SelectedItem + 0x10);
                byte[] command = BuildCommandFrame(address, 0x42, 0x3F);
                _serialPort.Write(command, 0, command.Length);

                // 接收数据（带超时检测）
                int timeout = 500; // 500ms超时
                int elapsed = 0;
                while (_serialPort.BytesToRead < 10 && elapsed < timeout)
                {
                    System.Threading.Thread.Sleep(50);
                    elapsed += 50;
                }

                if (_serialPort.BytesToRead >= 10)
                {
                    byte[] buffer = new byte[10];
                    _serialPort.Read(buffer, 0, 10);
                    double weight = ParseWeightData(buffer);
                    UpdateWeightDisplay(weight);
                    _lastResponseTime = DateTime.Now;
                    textBox1.BackColor = Color.LightGreen;
                }
                else
                {
                    // 连续3次无响应视为断开
                    if ((DateTime.Now - _lastResponseTime).TotalSeconds > 3)
                    {
                        MessageBox.Show("传感器连接超时，请检查设备！");
                        ResetConnection();
                    }
                    textBox1.BackColor = Color.LightCoral;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据读取失败：{ex.Message}");
                ResetConnection();
            }
        }

        // 构建命令帧（地址 + 命令 + 参数 + 校验和 + 结束符）
        private byte[] BuildCommandFrame(byte address, byte commandCode, byte parameter)
        {
            byte[] frame = new byte[5];
            frame[0] = address;
            frame[1] = commandCode;
            frame[2] = parameter;
            frame[3] = CalculateChecksum(address, commandCode, parameter);
            frame[4] = 0x0D; // 结束符
            return frame;
        }

        // 计算校验和（低7位，若为0x0D则+1）
        private byte CalculateChecksum(byte address, byte command, byte parameter)
        {
            int sum = address + command + parameter;
            byte checksum = (byte)(sum & 0x7F);
            return checksum == 0x0D ? (byte)(checksum + 1) : checksum;
        }

        // 解析重量数据（示例解析逻辑）
        private double ParseWeightData(byte[] data)
        {
            // 1. 计算原始数值（必须保留此部分）
            int rawValue = (data[5] - 0x30) * 65536 +
                           (data[6] - 0x30) * 4096 +
                           (data[7] - 0x30) * 256 +
                           (data[8] - 0x30) * 16 +
                           (data[9] - 0x30);

            // 2. 解析符号和小数点
            byte status = data[10];
            bool isNegative = (status & 0x04) != 0;

            // 3. 传统switch写法
            int decimalPlaces;
            switch (status & 0x03)
            {
                case 0b11:
                    decimalPlaces = 3;
                    break;
                case 0b10:
                    decimalPlaces = 2;
                    break;
                case 0b01:
                    decimalPlaces = 1;
                    break;
                default:
                    decimalPlaces = 0;
                    break;
            }

            // 4. 计算最终重量
            double weight = rawValue / Math.Pow(10, decimalPlaces);
            return isNegative ? -weight : weight;
        }

        // 更新显示（UI线程安全）
        private void UpdateWeightDisplay(double weight)
        {
            if (textBox1.InvokeRequired)
            {
                textBox1.Invoke(new Action(() =>
                {
                    textBox1.Text = $"{weight:F3} kg"; // 保留3位小数
                }));
            }
            else
            {
                textBox1.Text = $"{weight:F3} kg";
            }
        }

        // 点击“清空结果”按钮
        private void button2_Click_1(object sender, EventArgs e)
        {
            textBox1.Clear();
        }

        // 点击“退出系统”按钮
        private void button3_Click(object sender, EventArgs e)
        {
            _samplingTimer?.Stop();
            _serialPort?.Close();
            Application.Exit();
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
                
        }
    }

    }