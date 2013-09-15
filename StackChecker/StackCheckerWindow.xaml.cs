﻿using System;
using System.Windows.Controls;
using Atmel.Studio.Services;
using Atmel.Studio.Services.Device;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System.Windows;

namespace FourWalledCubicle.StackChecker
{
    public partial class StackCheckerWindow : UserControl
    {
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
            string startupProjectName = string.Empty;
            SolutionBuild solutionBuild = mDTE.Solution.SolutionBuild;

            if ((solutionBuild == null) || (solutionBuild.StartupProjects == null))
                return;

            foreach (String projectName in (Array)solutionBuild.StartupProjects)
            {
                Project project = mDTE.Solution.Projects.Item(projectName);

                try
                {
                    string instrumentFileLocation = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "_StackInstrument.c");

                    System.IO.StreamWriter instrumentCode = new System.IO.StreamWriter(instrumentFileLocation);
                    instrumentCode.Write(StackChecker.Resources.InstrumentationCode);
                    instrumentCode.Close();

                    project.ProjectItems.AddFromFileCopy(instrumentFileLocation);
                }
                catch { }
            }
        }

        private void refreshUsage_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            UpdateUI();

            if (mDTE.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
                Dispatcher.Invoke(new Action(UpdateStackUsageInfo));
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
                    deviceName.Text = "(Target is running)";
                    stackUsageVal.Text = "(Target is running)";
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
            foreach (IAddressSpace mem in target.Device.AddressSpaces)
            {
                foreach (IMemorySegment seg in mem.MemorySegments)
                {
                    if (seg.Type.IndexOf("RAM", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if ((seg.Name.IndexOf("IRAM", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (seg.Name.IndexOf("INTERNAL_SRAM", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (seg.Name.IndexOf("INTRAM0", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (seg.Name.IndexOf("HRAMC0", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        addressSpace = mem;
                        memorySegment = seg;
                        return true;
                    }
                }
            }

            addressSpace = null;
            memorySegment = null;
            return false;
        }
    }
}