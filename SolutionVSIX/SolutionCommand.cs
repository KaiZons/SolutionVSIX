using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SolutionVSIX
{
    /*
     * https://docs.microsoft.com/en-us/visualstudio/extensibility/internals/how-vspackages-add-user-interface-elements?view=vs-2019
     * https://docs.microsoft.com/zh-cn/visualstudio/extensibility/creating-an-extension-with-a-menu-command?view=vs-2019
     * https://docs.microsoft.com/en-us/visualstudio/extensibility/adding-a-menu-to-the-visual-studio-menu-bar?view=vs-2019
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
        public const int OpenFoowwTodayLogID = 0x0101;
        public const int OpenFoowwLocalDatabaseID = 0x0102;
        public const int OpenFoowwLocalLargeDatabaseID = 0x0103;
        public const int ChangeFoowwEnvironmentToFormalID = 0x0104;
        public const int ChangeFoowwEnvironmentToTestID = 0x0105;

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
            AddCommand(OpenFoowwTodayLogID, this.OpenFoowwTodayLog);
            AddCommand(OpenFoowwLocalDatabaseID, this.OpenFoowwLocalDatabase);
            AddCommand(OpenFoowwLocalLargeDatabaseID, this.OpenFoowwLocalLargeDatabase);
            AddCommand(ChangeFoowwEnvironmentToFormalID, this.ChangeFoowwEnvironmentToFormal);
            AddCommand(ChangeFoowwEnvironmentToTestID, this.ChangeFoowwEnvironmentToTest);
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

            var dte2 = Package.GetGlobalService(typeof(DTE)) as DTE2;
            var solution = dte2.Solution;

            var projects = (UIHierarchyItem[])dte2?.ToolWindows.SolutionExplorer.SelectedItems;
            var project = projects[0].Object as Project;


            var SolutionName = Path.GetFileName(solution.FullName);//解决方案名称
            var SolutionDir = Path.GetDirectoryName(solution.FullName);//解决方案路径
            var ProjectName = Path.GetFileName(project.FullName);//项目名称
            var ProjectDir = Path.GetDirectoryName(project.FullName);//项目路径
        }

        private void OpenFoowwTodayLog(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.OpenFoowwTodayLog()", this.GetType().FullName);
            string title = "SolutionCommand";

            // Show a message box to prove we were here
            VsShellUtilities.ShowMessageBox(
                this.package,
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private void OpenFoowwLocalDatabase(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.OpenFoowwTodayLog()", this.GetType().FullName);
            string title = "SolutionCommand";

            // Show a message box to prove we were here
            VsShellUtilities.ShowMessageBox(
                this.package,
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private void OpenFoowwLocalLargeDatabase(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.OpenFoowwTodayLog()", this.GetType().FullName);
            string title = "SolutionCommand";

            // Show a message box to prove we were here
            VsShellUtilities.ShowMessageBox(
                this.package,
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private void ChangeFoowwEnvironmentToFormal(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.OpenFoowwTodayLog()", this.GetType().FullName);
            string title = "SolutionCommand";

            // Show a message box to prove we were here
            VsShellUtilities.ShowMessageBox(
                this.package,
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private void ChangeFoowwEnvironmentToTest(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.OpenFoowwTodayLog()", this.GetType().FullName);
            string title = "SolutionCommand";

            // Show a message box to prove we were here
            VsShellUtilities.ShowMessageBox(
                this.package,
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
