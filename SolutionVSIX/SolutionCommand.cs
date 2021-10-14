using System;
using System.ComponentModel.Design;
using System.Configuration;
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
     * 扩展安装方式：https://blog.csdn.net/u013986317/article/details/114226288
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

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("ca807c56-c7e2-4e05-8b23-cc42d6163252");
        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        private readonly OleMenuCommandService m_commandService;

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
        }

        private void AddCommand(int commandID, EventHandler handler)
        {
            var menuCommandID = new CommandID(CommandSet, commandID);
            var menuCommand = new MenuCommand(handler, menuCommandID);
            m_commandService.AddCommand(menuCommand);
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
                MessageBox.Show("空白数据库替换成功！开始搬砖吧！");
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

        private void GetAllProjects()
        {
            // 获取所有项目
            //var dte2 = Package.GetGlobalService(typeof(DTE)) as DTE2;
            //var solution = dte2.Solution;

            //var projects = (UIHierarchyItem[])dte2?.ToolWindows.SolutionExplorer.SelectedItems;
            //var project = projects[0].Object as Project;


            //var SolutionName = Path.GetFileName(solution.FullName);//解决方案名称
            //var SolutionDir = Path.GetDirectoryName(solution.FullName);//解决方案路径
            //var ProjectName = Path.GetFileName(project.FullName);//项目名称
            //var ProjectDir = Path.GetDirectoryName(project.FullName);//项目路径
        }
    }
}
