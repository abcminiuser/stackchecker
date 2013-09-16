using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Atmel.Studio.Services;
using Atmel.Studio.Services.Device;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace FourWalledCubicle.StackChecker
{
    public partial class StackCheckerWindow : UserControl
    {
        private const string STACK_INSTRUMENT_FILENAME = "_StackInstrument.c";

        private DTE mDTE;
        private DebuggerEvents mDebuggerEvents;
        private ITargetService2 mTargetService;

        public StackCheckerWindow()
        {
            InitializeComponent();

            mDTE = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (mDTE == null)
                return;

            mTargetService = ATServiceProvider.TargetService2;

            mDebuggerEvents = mDTE.Events.DebuggerEvents;
            mDebuggerEvents.OnEnterRunMode += mDebuggerEvents_OnEnterRunMode;
            mDebuggerEvents.OnEnterBreakMode += mDebuggerEvents_OnEnterBreakMode;
            mDebuggerEvents.OnEnterDesignMode += mDebuggerEvents_OnEnterDesignMode;

            UpdateUI();
        }

        private void addInstrumentCode_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SolutionBuild solutionBuild = mDTE.Solution.SolutionBuild;
            if ((solutionBuild == null) || (solutionBuild.StartupProjects == null))
                return;

            foreach (String projectName in (Array)solutionBuild.StartupProjects)
            {
                Project project = mDTE.Solution.Projects.Item(projectName);
                if (project == null)
                    return;

                try
                {
                    string instrumentFileLocation = Path.Combine(Path.GetTempPath(), STACK_INSTRUMENT_FILENAME);

                    StreamWriter instrumentCode = new StreamWriter(instrumentFileLocation);
                    instrumentCode.Write(StackChecker.Resources.InstrumentationCode);
                    instrumentCode.Close();

                    ProjectItem existingInstrumentCode = project.ProjectItems.Item(STACK_INSTRUMENT_FILENAME);
                    if (existingInstrumentCode != null)
                        existingInstrumentCode.Document.Close();

                    project.ProjectItems.AddFromFileCopy(instrumentFileLocation);
                    project.ProjectItems.Item(STACK_INSTRUMENT_FILENAME).Open(Constants.vsViewKindCode).Visible = true;

                    if (existingInstrumentCode == null)
                    {
                        ATServiceProvider.DialogService.ShowDialog(
                            null,
                            "Instrumenting code has been added to your project as a new source file." + Environment.NewLine + "Please recompile your project to enable.",
                            "Stack Checker - Instrumenting Code Added",
                            DialogButtonSet.Ok, DialogIcon.Information);
                    }
                }
                catch { }
            }
        }

        private void refreshUsage_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            UpdateUI();

            SolutionBuild solutionBuild = mDTE.Solution.SolutionBuild;
            if ((solutionBuild != null) && (solutionBuild.StartupProjects != null))
            {
                foreach (String projectName in (Array)solutionBuild.StartupProjects)
                {
                    Project project = mDTE.Solution.Projects.Item(projectName);
                    if (project == null)
                        return;

                    if (project.ProjectItems.Item(STACK_INSTRUMENT_FILENAME) == null)
                    {
                        deviceName.Text = "(Missing Instrumentation)";
                        stackUsageVal.Text = "(Missing Instrumentation)";
                        return;
                    }
                }
            }

            if (mDTE.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
                Dispatcher.Invoke(new Action(UpdateStackUsageInfo));
        }

        private void helpInfo_Click(object sender, RoutedEventArgs e)
        {
            ATServiceProvider.DialogService.ShowDialog(
                null,
                "This stack checker shows the maximum stack usage of your application, based on the currently " +
                "running debug session. It is not guaranteed to be an upper bound; the value shown is the high " +
                "water mark for the current debug session only." +
                Environment.NewLine + Environment.NewLine +
                "To use, click the Add Instrumentation button, recompile and run your project. Pause execution " +
                "and click the Refresh button to determine the current stack high water mark." +
                Environment.NewLine + Environment.NewLine +
                "Currently only 8-bit AVR devices (TINY/MEGA/XMEGA) are supported.",
                "Stack Checker - Usage",
                DialogButtonSet.Ok, DialogIcon.Information);
        }

        void mDebuggerEvents_OnEnterRunMode(dbgEventReason Reason)
        {
            UpdateUI();
        }

        void mDebuggerEvents_OnEnterBreakMode(dbgEventReason Reason, ref dbgExecutionAction execAction)
        {
            UpdateUI();
        }

        void mDebuggerEvents_OnEnterDesignMode(dbgEventReason Reason)
        {
            UpdateUI();
        }

        void UpdateUI()
        {
            stackUsageProgress.Maximum = 100;
            stackUsageProgress.Value = 0;

            deviceName.FontStyle = FontStyles.Italic;
            stackUsageVal.FontStyle = FontStyles.Italic;

            switch (mDTE.Debugger.CurrentMode)
            {
                case dbgDebugMode.dbgBreakMode:
                    deviceName.Text = "(Refresh Required)";
                    stackUsageVal.Text = "(Refresh Required)";
                    break;

                case dbgDebugMode.dbgRunMode:
                    deviceName.Text = "(Target Running)";
                    stackUsageVal.Text = "(Target Running)";
                    break;

                default:
                    deviceName.Text = "(Not in Debug Session)";
                    stackUsageVal.Text = "(Not in Debug Session)";
                    break;
            }
        }

        void UpdateStackUsageInfo()
        {
            ITarget2 target = mTargetService.GetLaunchedTarget();
            if (target == null)
                return;

            IAddressSpace internalSRAMSpace;
            IMemorySegment internalSRAMSegment;
            if (GetInternalSRAM(target, out internalSRAMSpace, out internalSRAMSegment))
            {
                ulong currentUsage, maxUsage;
                GetStackUsage(target, internalSRAMSpace, internalSRAMSegment, out currentUsage, out maxUsage);

                stackUsageProgress.Maximum = maxUsage;
                stackUsageProgress.Value = currentUsage;

                deviceName.FontStyle = FontStyles.Normal;
                stackUsageVal.FontStyle = FontStyles.Normal;

                deviceName.Text = target.Device.Name;
                stackUsageVal.Text = string.Format("{0}/{1} ({2}%)",
                    stackUsageProgress.Value.ToString(), stackUsageProgress.Maximum.ToString(),
                    Math.Min(100, Math.Ceiling((100.0 * stackUsageProgress.Value) / stackUsageProgress.Maximum)));
            }
            else
            {
                deviceName.Text = target.Device.Name;
                stackUsageVal.Text = "(Unsupported Device)";
            }
        }

        void GetStackUsage(ITarget2 target, IAddressSpace addressSpace, IMemorySegment memorySegment, out ulong current, out ulong max)
        {
            try
            {
                MemoryErrorRange[] errorRange;
                byte[] result = target.GetMemory(
                    target.GetAddressSpaceName(addressSpace.Name),
                    memorySegment.Start, 1, (int)memorySegment.Size, 0, out errorRange);

                ulong? start = null;
                ulong? end = null;

                for (int i = (result.Length - 1); i >= 0; i -= 4)
                {
                    if ((result[i - 0] == 0xDC) && (result[i - 1] == 0xDC) && (result[i - 2] == 0xDC) && (result[i - 3] == 0xDC))
                    {
                        if (start.HasValue == false)
                            start = (ulong)i;
                    }
                    else if (start.HasValue)
                    {
                        end = (ulong)i;
                        break;
                    }
                }

                current = memorySegment.Size - (start ?? 0);
                max = memorySegment.Size - (end ?? 0);
            }
            catch
            {
                current = 0;
                max = memorySegment.Size;
            }
        }

        bool GetInternalSRAM(ITarget2 target, out IAddressSpace addressSpace, out IMemorySegment memorySegment)
        {
            addressSpace = null;
            memorySegment = null;

            if (target.Device.Architecture.StartsWith("AVR8") == false)
                return false;

            foreach (IAddressSpace mem in target.Device.AddressSpaces)
            {
                foreach (IMemorySegment seg in mem.MemorySegments)
                {
                    if (seg.Type.IndexOf("RAM", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if ((seg.Name.IndexOf("IRAM", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (seg.Name.IndexOf("INTERNAL_SRAM", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        addressSpace = mem;
                        memorySegment = seg;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
