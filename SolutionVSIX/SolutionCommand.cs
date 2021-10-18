using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Configuration;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Configuration = System.Configuration.Configuration;
using ConfigurationManager = System.Configuration.ConfigurationManager;
using Task = System.Threading.Tasks.Task;

namespace SolutionVSIX
{
    /*
     * 要调试，需要在vs中，选择调试-选项-常规中的使用托管兼容模式勾选。
     * 
     * https://docs.microsoft.com/en-us/visualstudio/extensibility/internals/how-vspackages-add-user-interface-elements?view=vs-2019
     * https://docs.microsoft.com/zh-cn/visualstudio/extensibility/creating-an-extension-with-a-menu-command?view=vs-2019
     * https://docs.microsoft.com/en-us/visualstudio/extensibility/adding-a-menu-to-the-visual-studio-menu-bar?view=vs-2019
     * 
     * 获取项目启动项目录可参考：https://codeleading.com/article/80471988456/
     * 
     * 创建输出窗口可参考：https://cloud.tencent.com/developer/article/1402381
     * https://docs.microsoft.com/zh-cn/visualstudio/extensibility/extending-the-properties-task-list-output-and-options-windows?view=vs-2019
     * 
     * 关于各种通知方式的介绍：https://docs.microsoft.com/zh-cn/visualstudio/extensibility/ux-guidelines/notifications-and-progress-for-visual-studio?view=vs-2019
     * 其中提到“通知”类不允许扩展
     * 
     * 扩展安装方式：https://blog.csdn.net/u013986317/article/details/114226288
     * 
     */
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class SolutionCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int OpenFoowwDebugID = 0x0100;
        public const int OpenFoowwLogID = 0x0101;
        public const int OpenFoowwLocalDatabaseID = 0x0102;
        public const int OpenFoowwLocalLargeDatabaseID = 0x0103;
        public const int ChangeFoowwEnvironmentToFormalID = 0x0104;
        public const int ChangeFoowwEnvironmentToTestID = 0x0105;
        public const int ReplaceFoowwDatabaseID = 0x0106;
        public const int PrintFoowwLogToOutputWindowID = 0x0107;
        public const int DisablePrintFoowwLogToOutputWindowID = 0x0108;

        /// <summary>
        /// Command menu group (command set GUID). 放到一个组里
        /// </summary>
        public static readonly Guid CommandSet = new Guid("ca807c56-c7e2-4e05-8b23-cc42d6163252");

        public static readonly Guid CustomOutputWindowPane = new Guid("4d564d14-ae50-45b4-a882-4c64d9de1526");
        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        private readonly OleMenuCommandService m_commandService;

        private FileSystemWatcher m_fileWatcher;

        /// <summary>
        /// Initializes a new instance of the <see cref="SolutionCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private SolutionCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            m_commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
            AddCommand(OpenFoowwDebugID, this.OpenFoowwDebug);
            AddCommand(OpenFoowwLogID, this.OpenFoowwLog);
            AddCommand(OpenFoowwLocalDatabaseID, this.OpenFoowwLocalDatabase);
            AddCommand(OpenFoowwLocalLargeDatabaseID, this.OpenFoowwLocalLargeDatabase);
            AddCommand(ChangeFoowwEnvironmentToFormalID, this.ChangeFoowwEnvironmentToFormal);
            AddCommand(ChangeFoowwEnvironmentToTestID, this.ChangeFoowwEnvironmentToTest);
            AddCommand(ReplaceFoowwDatabaseID, this.ReplaceFoowwDatabase);
            AddCommand(PrintFoowwLogToOutputWindowID, this.PrintFoowwLogToOutputWindow);
            AddCommand(DisablePrintFoowwLogToOutputWindowID, this.DisablePrintFoowwLogToOutputWindow);

            // 创建输出窗口
            CreatePane(CustomOutputWindowPane, "FoowwSoftLog", true, true);
            InitFileWatcher();
        }

        private void AddCommand(int commandID, EventHandler handler)
        {
            var menuCommandID = new CommandID(CommandSet, commandID);
            var menuCommand = new MenuCommand(handler, menuCommandID);
            m_commandService.AddCommand(menuCommand);
        }

        /// <summary>
        /// 创建输出窗体
        /// </summary>
        /// <param name="paneGuid">自定义ID</param>
        /// <param name="title">输出窗口的类型</param>
        /// <param name="visible">是否最初可见</param>
        /// <param name="clearWithSolution">关闭解决方案时清空</param>
        private void CreatePane(Guid paneGuid, string title, bool visible, bool clearWithSolution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IVsOutputWindow output = (IVsOutputWindow)Package.GetGlobalService(typeof(SVsOutputWindow));

            // Create a new pane.  
            // 参考：https://docs.microsoft.com/zh-cn/dotnet/api/microsoft.visualstudio.shell.interop.ivsoutputwindow.createpane?view=visualstudiosdk-2019
            output.CreatePane(
                ref paneGuid,
                title,
                Convert.ToInt32(visible),
                Convert.ToInt32(clearWithSolution));
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static SolutionCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in SolutionCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new SolutionCommand(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void OpenFoowwDebug(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            System.Diagnostics.Process.Start("explorer.exe", $@"/select,{GetStartupProjectDirectoryPath() + @"\bin\x86\Debug\FoowwSoft.exe"}");
        }

        private void OpenFoowwLog(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            System.Diagnostics.Process.Start("explorer.exe", GetStartupProjectDirectoryPath() + @"\bin\x86\Debug\Logs\");
        }

        private void OpenFoowwLocalDatabase(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            System.Diagnostics.Process.Start(GetStartupProjectDirectoryPath() + @"\bin\x86\Debug\FoowwCE.sdf");
        }

        private void OpenFoowwLocalLargeDatabase(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            System.Diagnostics.Process.Start(GetStartupProjectDirectoryPath() + @"\bin\x86\Debug\FoowwCELarge.sdf");
        }

        private void ChangeFoowwEnvironmentToFormal(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string appConfigPath = GetStartupProjectDirectoryPath() + @"\app.config";
            string environmentNode = "Environment";
            ExeConfigurationFileMap configMap = new ExeConfigurationFileMap();
            configMap.ExeConfigFilename = appConfigPath;
            Configuration appConfig = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);
            // 没有配置，默认正式
            if (!appConfig.AppSettings.Settings.AllKeys.Contains(environmentNode))
            {
                return;
            }

            // 配置是test，则需要改为正式
            string value = appConfig.AppSettings.Settings[environmentNode].Value;
            if (value.ToLower() == "test")
            {
                appConfig.AppSettings.Settings[environmentNode].Value = value + "1";
                appConfig.Save(ConfigurationSaveMode.Modified);
                //使connectionStrings配置节缓存失效，下次必须从磁盘读取
                ConfigurationManager.RefreshSection("appSettings");
            }

            OutputTextToStatusbar("已切换到正式环境！");
        }

        private void ChangeFoowwEnvironmentToTest(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string appConfigPath = GetStartupProjectDirectoryPath() + @"\app.config";
            string environmentNode = "Environment";
            ExeConfigurationFileMap configMap = new ExeConfigurationFileMap();
            configMap.ExeConfigFilename = appConfigPath;
            Configuration appConfig = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);
            // 没有配置，需要新增测试环境配置
            if (!appConfig.AppSettings.Settings.AllKeys.Contains(environmentNode))
            {
                appConfig.AppSettings.Settings.Add(environmentNode, "Test");
                appConfig.Save(ConfigurationSaveMode.Modified);
                //使connectionStrings配置节缓存失效，下次必须从磁盘读取
                ConfigurationManager.RefreshSection("appSettings");
                return;
            }

            // 否则改为测试
            string value = appConfig.AppSettings.Settings[environmentNode].Value;
            if (value.ToLower() != "test")
            {
                appConfig.AppSettings.Settings[environmentNode].Value = "Test";
                appConfig.Save(ConfigurationSaveMode.Modified);
                //使connectionStrings配置节缓存失效，下次必须从磁盘读取
                ConfigurationManager.RefreshSection("appSettings");
            }

            OutputTextToStatusbar("已切换到测试环境！");
        }

        private void ReplaceFoowwDatabase(object sender, EventArgs e)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                string debugPath = GetStartupProjectDirectoryPath() + @"\bin\x86\Debug";
                string tempDirectoryPath = debugPath + @"\FoowwVsixTemp";
                if (Directory.Exists(tempDirectoryPath))
                {
                    Directory.Delete(tempDirectoryPath, true);
                }
                Directory.CreateDirectory(tempDirectoryPath);
                Uri foowwCEUri = new Uri(@"\\lab04\研发部\PC组\梵讯房屋管理系统\软件打包工具\FoowwCE.sdf");
                Uri foowwCELargeUri = new Uri(@"\\lab04\研发部\PC组\梵讯房屋管理系统\软件打包工具\FoowwCELarge.sdf");
                using (WebClient webClient = new WebClient())
                {
                    webClient.DownloadFile(foowwCEUri, tempDirectoryPath + @"\FoowwCE.sdf");
                    webClient.DownloadFile(foowwCELargeUri, tempDirectoryPath + @"\FoowwCELarge.sdf");
                }

                DirectoryInfo tempDirectory = new DirectoryInfo(tempDirectoryPath);
                foreach (FileInfo file in tempDirectory.GetFiles())
                {
                    string targetFileName = Path.Combine(debugPath, file.Name);
                    File.Copy(file.FullName, targetFileName, true);
                }
                Directory.Delete(tempDirectoryPath, true);

                OutputTextToStatusbar("空白库替换成功！");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.InnerException.Message + ex.StackTrace + ex.Message);
            }
        }

        private void PrintFoowwLogToOutputWindow(object sender, EventArgs e)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                // 状态栏提示
                string message = "已开启打印Log到输出窗口！Log有更新时，会实时打印到输出窗口！";
                OutputTextToStatusbar(message);
                m_fileWatcher.Path = GetStartupProjectDirectoryPath() + @"\bin\x86\Debug\Logs\";
                m_fileWatcher.EnableRaisingEvents = true;

                // 打印最新日志
                ClearOutputWindowPane();
                OutputTextToOutputWindowPane(string.Empty, "拉取到最新日志如下：");
                DirectoryInfo directoryInfo = new DirectoryInfo(m_fileWatcher.Path);
                FileInfo[] files = directoryInfo.GetFiles();
                if (files != null && files.Length > 0)
                {
                    FileInfo lastWriteFile = directoryInfo.GetFiles().OrderByDescending(a => a.LastWriteTime).First();
                    PrintLog(lastWriteFile.Name, lastWriteFile.FullName, false);
                }
                else
                {
                    PrintLog(string.Empty, string.Empty, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.InnerException.Message + ex.StackTrace + ex.Message);
            }
        }

        private void DisablePrintFoowwLogToOutputWindow(object sender, EventArgs e)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                string message = "已关闭打印Log到输出窗口的功能！";
                OutputTextToStatusbar(message);
                OutputTextToOutputWindowPane(string.Empty, message);
                m_fileWatcher.EnableRaisingEvents = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.InnerException.Message + ex.StackTrace + ex.Message);
            }
        }

        private string GetStartupProjectDirectoryPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Task<object> serviceTask = this.package.GetServiceAsync(typeof(SVsSolutionBuildManager));
            var StartBuid = (IVsSolutionBuildManager2)serviceTask.Result;
            StartBuid.get_StartupProject(out IVsHierarchy startupProject);
            if (startupProject == null)
            {
                MessageBox.Show("未检测到启动项，请确保是否已打开解决方案");
                throw new Exception("未检测到启动项，请确保是否已打开解决方案");
            }
            //StartBuid.StartUpdateProjectConfigurations(1, new[] { startupProject }, (uint)VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_BUILD, 0);//编译
            startupProject.GetProperty((uint)VSConstants.VSITEMID.Root, (int)__VSHPROPID.VSHPROPID_ExtObject, out var obj);
            Project project = obj as Project;
            if (project == null)
            {
                throw new Exception("检索启动项异常");
            }
            return new FileInfo(project.FileName).DirectoryName;
        }

        private void InitFileWatcher()
        {
            m_fileWatcher = new FileSystemWatcher();
            m_fileWatcher.NotifyFilter = NotifyFilters.LastWrite;
            m_fileWatcher.Filter = "*.txt";
            m_fileWatcher.Changed += OnFileWatcherChanged;
            m_fileWatcher.IncludeSubdirectories = false;
            m_fileWatcher.EnableRaisingEvents = false;
        }

        private void OnFileWatcherChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Changed)
            {
                return;
            }
            // UI线程执行
            ThreadHelper.Generic.BeginInvoke(() =>
            {
                OutputTextToStatusbar($"[{DateTime.Now}]文件[{e.Name}]" + "有新日志产生，请查看FoowwSoftLog输出窗口！！！");
                PrintLog(e.Name, e.FullPath);
            });
        }

        private void PrintLog(string fileName, string fileFullPath, bool isClearOutputWindowPane = true)
        {
            if (isClearOutputWindowPane)
            {
                ClearOutputWindowPane();
            }
            
            if (!string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(fileFullPath))
            {
                string text = System.IO.File.ReadAllText(fileFullPath);
                OutputTextToOutputWindowPane($"   日志文件：{fileName}", text);
            }
            else
            {
                OutputTextToOutputWindowPane(string.Empty, string.Empty);
            }
        }

        /// <summary>
        /// 输出到 自定义的输出窗口
        /// </summary>
        /// <param name="title"></param>
        /// <param name="text"></param>
        private void OutputTextToOutputWindowPane(string title, string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IVsOutputWindow output = (IVsOutputWindow)Package.GetGlobalService(typeof(SVsOutputWindow));
            output.GetPane(CustomOutputWindowPane, out IVsOutputWindowPane pane);
            pane.Activate(); // 如果窗口被隐藏了,则可以激活显示;如果本身输出窗口就没有打开,该方法并不会强制打开输出窗口

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            pane.OutputString("\r\n");
            pane.OutputString($"=========================输出时间：{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{title}=========================");
            pane.OutputString("\r\n");
            pane.OutputString(text);
            pane.OutputString("\r\n");
            pane.OutputString("==============================================END===============================================");
        }

        /// <summary>
        /// 清空输出窗口
        /// </summary>
        private void ClearOutputWindowPane()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsOutputWindow output = (IVsOutputWindow)Package.GetGlobalService(typeof(SVsOutputWindow));
            output.GetPane(CustomOutputWindowPane, out IVsOutputWindowPane pane);

            pane.Clear();
        }

        /// <summary>
        /// 输出到 状态栏
        /// </summary>
        /// <param name="text"></param>
        private void OutputTextToStatusbar(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 状态栏：https://docs.microsoft.com/zh-cn/visualstudio/extensibility/ux-guidelines/notifications-and-progress-for-visual-studio?view=vs-2019
            IVsStatusbar statusBar = (IVsStatusbar)ServiceProvider.GetServiceAsync(typeof(SVsStatusbar)).Result;
            
            //这个方法设置了颜色也不管用，参考官网
            statusBar.SetColorText(text, 0, 0);
        }
    }
}
