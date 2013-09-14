using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Atmel.Studio.Services;
using Atmel.Studio.Services.Device;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System.Linq;

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
            mDebuggerEvents.OnEnterBreakMode += mDebuggerEvents_OnEnterBreakMode;

            mTargetService = ATServiceProvider.TargetService2;
        }

        void mDebuggerEvents_OnEnterBreakMode(dbgEventReason Reason, ref dbgExecutionAction ExecutionAction)
        {
            ulong stackStart = 0;
            ulong stackCurrent = 0;

            ITarget2 target = mTargetService.GetLaunchedTarget();
            IAddressSpace internalSRAMSpace;
            IMemorySegment internalSRAMSegment;
            if (GetInternalSRAM(target, out internalSRAMSpace, out internalSRAMSegment) == true)
            {
                stackStart = internalSRAMSegment.Size;

                stackCurrent = stackStart;
                MemoryErrorRange[] errorRange;
                byte[] result = target.GetMemory(
                    target.GetAddressSpaceName(internalSRAMSpace.Name),
                    internalSRAMSegment.Start, 1, (int)internalSRAMSegment.Size, 0, out errorRange);

                for (int i = (result.Length - 1); i >= 0; i -= 4)
                {
                    if ((result[i - 0] == 0xDC) && (result[i - 1] == 0xDC) && (result[i - 2] == 0xDC) && (result[i - 3] == 0xDC))
                    {
                        stackCurrent = (ulong)i;
                        break;
                    }
                }
            }

            deviceName.Text = target.Device.Name;

            stackUsageProgress.Maximum = stackStart;
            stackUsageProgress.Value = (stackStart - stackCurrent);

            stackUsageVal.Text = string.Format("{0}/{1} ({2}%)",
                stackUsageProgress.Value.ToString(), stackUsageProgress.Maximum.ToString(),
                Math.Min(100, Math.Ceiling((100.0 * stackUsageProgress.Value) / stackUsageProgress.Maximum)));
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