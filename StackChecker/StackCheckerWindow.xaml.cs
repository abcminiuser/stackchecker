﻿using System;
using System.Windows.Controls;
using Atmel.Studio.Services;
using Atmel.Studio.Services.Device;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

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

            mDebuggerEvents = mDTE.Events.DebuggerEvents;
            mDebuggerEvents.OnEnterRunMode += mDebuggerEvents_OnEnterRunMode;
            mDebuggerEvents.OnEnterBreakMode += mDebuggerEvents_OnEnterBreakMode;

            mTargetService = ATServiceProvider.TargetService2;
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

                    project.ProjectItems.AddFromFile(instrumentFileLocation);
                }
                catch { }
            }
        }

        void mDebuggerEvents_OnEnterRunMode(dbgEventReason Reason)
        {
            stackUsageProgress.Maximum = 100;
            stackUsageProgress.Value = 0;
            deviceName.Text = "(Break execution to refresh)";
            stackUsageVal.Text = "(Break execution to refresh)";
        }

        void mDebuggerEvents_OnEnterBreakMode(dbgEventReason Reason, ref dbgExecutionAction ExecutionAction)
        {
            stackUsageProgress.Maximum = 100;
            stackUsageProgress.Value = 0;
            deviceName.Text = "N/A";
            stackUsageVal.Text = "N/A";

            Dispatcher.Invoke(new Action(() => UpdateStackUsageInfo()));
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
                stackUsageProgress.Maximum = internalSRAMSegment.Size;
                stackUsageProgress.Value = GetMaximumStackUsage(target, internalSRAMSpace, internalSRAMSegment);

                deviceName.Text = target.Device.Name;
                stackUsageVal.Text = string.Format("{0}/{1} ({2}%)",
                    stackUsageProgress.Value.ToString(), stackUsageProgress.Maximum.ToString(),
                    Math.Min(100, Math.Ceiling((100.0 * stackUsageProgress.Value) / stackUsageProgress.Maximum)));
            }
        }

        ulong GetMaximumStackUsage(ITarget2 target, IAddressSpace addressSpace, IMemorySegment memorySegment)
        {
            try
            {
                MemoryErrorRange[] errorRange;
                byte[] result = target.GetMemory(
                    target.GetAddressSpaceName(addressSpace.Name),
                    memorySegment.Start, 1, (int)memorySegment.Size, 0, out errorRange);

                for (int i = (result.Length - 1); i >= 0; i -= 4)
                {
                    if ((result[i - 0] == 0xDC) && (result[i - 1] == 0xDC) && (result[i - 2] == 0xDC) && (result[i - 3] == 0xDC))
                    {
                        return (memorySegment.Size - (ulong)i);
                    }
                }
            }
            catch { }

            return memorySegment.Size;
        }

        bool GetInternalSRAM(ITarget2 target, out IAddressSpace addressSpace, out IMemorySegment memorySegment)
        {
            foreach (IAddressSpace a in target.Device.AddressSpaces)
            {
                foreach (IMemorySegment s in a.MemorySegments)
                {
                    if ((s.Type.IndexOf("RAM", StringComparison.OrdinalIgnoreCase) >= 0) &&
                        (s.Name.IndexOf("INTERNAL", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        addressSpace = a;
                        memorySegment = s;
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