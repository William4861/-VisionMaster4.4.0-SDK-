using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using VM.Core;
using VM.PlatformSDKCS;
using VMControls.Winform.Release;

namespace 我的VM二次开发
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        VmProcedure _procedure = null;
        VmRenderControl _renderLeft = new VmRenderControl();
        VmRenderControl _renderRight = new VmRenderControl();
        SerialPort mySerialPort;
        bool _isAtuo = false;


        public MainWindow()
        {
            InitializeComponent();
            this.leftHost.Child = _renderLeft;
            this.rightHost.Child = _renderRight;
            mySerialPort = new SerialPort();

            this.Closed += MainWindow_Closed;
        }

        //加载方案按钮
        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                //检查方案地址
                string soluPath = soluPathText.Text.Trim();
                if (!System.IO.File.Exists(soluPath))
                {
                    MessageBox.Show("方案不存在，请检查方案文件地址！");
                    return;
                }

                //设置运行目录与环境变量
                string vmAppPath = @"C:\Program Files\Vision Master4.4.0\Application";
                if (System.IO.Directory.Exists(vmAppPath))
                {
                    System.IO.Directory.SetCurrentDirectory(vmAppPath);
                    Environment.SetEnvironmentVariable("PATH", vmAppPath + ";" + Environment.GetEnvironmentVariable("PATH"));
                }

                
                //开始加载
                VmSolution.Load(soluPath);

                //读取流程1
                var list = VmSolution.Instance.GetAllProcedureList();
                string processName = list.nNum > 0 ? list.astProcessInfo[0].strProcessName : "流程1";
                _procedure = VmSolution.Instance[processName] as VmProcedure;

                //设置流程回调函数，绑定VM控件的来源模块
                if (_procedure != null)
                {
                    _procedure.OnWorkEndStatusCallBack += _procedure_OnWorkEndStatusCallBack;//设置流程结束的回调函数

                    //获取流程的所有模块
                    List<VmModule> vmModules = new List<VmModule>();
                    _procedure.GetAllModule(vmModules);

                    //遍历所有模块，绑定VM控件的对应模块
                    foreach (var module in vmModules)
                    {
                        //左侧图像绑定
                        if (module.FullName.Contains("输出图像3"))
                        {
                            _renderLeft.ModuleSource = module;
                            AppendLog($"左侧图像绑定成功:{module.FullName}");
                        }

                        //右侧图像绑定
                        if (module.FullName.Contains("输出图像2"))
                        {
                            _renderRight.ModuleSource = module;
                            AppendLog($"右侧图像绑定成功:{module.FullName}");
                        }
                    }

                    AppendLog("* * * 方案加载成功 * * *");
                    MessageBox.Show("方案加载成功");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"报错信息：{ex.Message}\n\n崩溃位置：\n{ex.StackTrace}");
            }
        }

        //流程运行结束回调函数
        private void _procedure_OnWorkEndStatusCallBack(object sender, EventArgs e)
        {
            try
            {
                string resultStr = "未获取到输出结果";// 初始化结果默认值（如果没找到参数，保持默认提示）
                var outputInfo = _procedure.ModuResult.GetAllOutputNameInfo();// 获取流程中所有输出参数的信息（名称、类型）
                foreach (var item in outputInfo)
                {
                    //
                    if (item.Name == "检测结果" && item.TypeName == IMVS_MODULE_BASE_DATA_TYPE.IMVS_GRAP_TYPE_STRING)
                    {
                        // 根据参数名获取字符串结果，并取出真正的文字值（OK/NG）
                        resultStr = _procedure.ModuResult.GetOutputString(item.Name).astStringVal[0].strValue;
                    }
                }

                //异步更新界面
                Dispatcher.Invoke(() =>
                {
                    statusText.Text = resultStr;
                    bool isOK = resultStr.ToUpper().Contains("OK");
                    statusBorder.Background = isOK ? Brushes.LimeGreen : Brushes.Red;
                    AppendLog($"[检测完成] 结果：{resultStr} 耗时：{_procedure.ProcessTime}ms");
                });

                //串口发送结果
                if (mySerialPort != null && mySerialPort.IsOpen)
                {
                    mySerialPort.WriteLine($"RESULT:{resultStr}");

                    Dispatcher.Invoke(() =>
                    {
                        AppendLog($"[串口发送] 已向 PLC 发送放行/剔除指令: RESULT:{resultStr}");
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => { AppendLog("回调异常：" + ex.Message); });
            }
            

        }

        //日志输出函数
        private void AppendLog(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                logText.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
                logText.ScrollToEnd();
            });
        }

        //单词执行按钮
        private void BtnRunOnce_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("上位机发送触发指令，相机开始采图...");
            statusBorder.Background = Brushes.LightGray;
            statusText.Text = "检测中...";

            _procedure?.Run();
        }

        //自动执行按钮
        private void btnRunAuto_Click(object sender, RoutedEventArgs e)
        {
            if(_procedure == null) return;//流程为空返回

            _isAtuo = !_isAtuo;//更新内部自动模式状态

            _procedure.ContinuousRunEnable = _isAtuo;//开/关流程自动模式

            //更新控件
            btnRunAuto.Content = _isAtuo ? "关闭自动执行" : "自动执行";

            //输出日志
            string msg = _isAtuo ? "上位机发送指令，开始自动执行..." : "上位机发送指令，已关闭自动执行";
            AppendLog(msg);
        }

        //打开串口按钮
        private void btnOpenSerial_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!mySerialPort.IsOpen)
                {
                    mySerialPort.PortName = txtComPort.Text.Trim(); // 获取界面的 COM 端口
                    mySerialPort.BaudRate = 9600;
                    mySerialPort.Open();

                    btnOpenSerial.Content = "关闭串口";
                    AppendLog($"串口 {mySerialPort.PortName} 已成功打开！准备与 PLC 通信。");
                }
                else
                {
                    mySerialPort.Close();
                    btnOpenSerial.Content = "打开串口";
                    AppendLog("串口已关闭。");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("串口打开失败：" + ex.Message);
            }
        }

        //窗口关闭函数
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            //安全关闭串口（防止串口被占用，下次打不开）
            if (mySerialPort != null && mySerialPort.IsOpen)
            {
                mySerialPort.Close();
            }

            //强制结束当前程序的所有底层线程
            Environment.Exit(0);
        }
    }
}
